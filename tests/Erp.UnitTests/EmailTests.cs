using Microsoft.Extensions.Configuration;
using Erp.Application.Abstractions;
using Erp.Modules.Platform.Application;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;

namespace Erp.UnitTests;

public class EmailTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    private static EmailOptions On() => new() { Enabled = true, Host = "smtp.test", FromAddress = "erp@example.com", BaseUrl = "https://erp.example.com" };

    // ---- options -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Email_is_off_by_default_and_needs_no_configuration()
    {
        var o = new EmailOptions();
        Assert.False(o.Enabled);
        Assert.Empty(o.Validate());   // an installation without email must start without any email settings
    }

    [Fact]
    public void A_complete_configuration_is_valid() => Assert.Empty(On().Validate());

    [Fact]
    public void Enabling_email_without_the_essentials_lists_every_problem()
    {
        var errors = new EmailOptions { Enabled = true }.Validate();
        Assert.Contains(errors, e => e.Contains("Host"));
        Assert.Contains(errors, e => e.Contains("FromAddress"));
        Assert.Contains(errors, e => e.Contains("BaseUrl"));
        Assert.All(errors, e => Assert.StartsWith("Email:", e));
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("")]
    [InlineData(null)]
    public void The_from_address_must_be_valid(string? from) =>
        Assert.Contains(new EmailOptions { Enabled = true, Host = "h", BaseUrl = "https://x.example", FromAddress = from }.Validate(), e => e.Contains("FromAddress"));

    [Theory]
    [InlineData("erp.example.com")]
    [InlineData("ftp://erp.example.com")]
    [InlineData("/relative")]
    public void The_base_url_must_be_an_absolute_http_address(string url)
    {
        var o = On(); o.BaseUrl = url;
        Assert.Contains(o.Validate(), e => e.Contains("BaseUrl"));
    }

    [Fact]
    public void A_username_needs_a_password()
    {
        var o = On(); o.Username = "mailer";
        Assert.Contains(o.Validate(), e => e.Contains("Password"));
        o.Password = "secret";
        Assert.Empty(o.Validate());
    }

    [Fact]
    public void Numeric_settings_are_bounded()
    {
        var o = On(); o.Port = 0; o.PollSeconds = 0; o.BatchSize = 0; o.MaxAttempts = 0; o.RetryMinutes = [-1]; o.KeepSentDays = 0; o.LeaseMinutes = 0;
        var errors = o.Validate();
        foreach (var name in new[] { "Port", "PollSeconds", "BatchSize", "MaxAttempts", "RetryMinutes", "KeepSentDays", "LeaseMinutes" })
            Assert.Contains(errors, e => e.Contains(name));
    }

    [Fact]
    public void Retry_delays_follow_the_schedule_and_stop_at_the_attempt_limit()
    {
        var o = new EmailOptions { MaxAttempts = 5, RetryMinutes = [1, 5, 30, 120, 720] };
        Assert.Equal(TimeSpan.FromMinutes(1), o.RetryDelay(1));
        Assert.Equal(TimeSpan.FromMinutes(5), o.RetryDelay(2));
        Assert.Equal(TimeSpan.FromMinutes(120), o.RetryDelay(4));
        Assert.Null(o.RetryDelay(5));        // fifth failure: give up
        Assert.Null(o.RetryDelay(9));
        Assert.Equal(TimeSpan.FromMinutes(5), new EmailOptions { MaxAttempts = 9, RetryMinutes = [1, 5] }.RetryDelay(7));   // a short list repeats its last value
        Assert.Null(new EmailOptions { RetryMinutes = [] }.RetryDelay(1));     // an explicit empty schedule means no retries
        Assert.Equal(TimeSpan.FromMinutes(1), new EmailOptions().RetryDelay(1));   // nothing configured: the defaults
        Assert.Equal(EmailOptions.DefaultRetryMinutes, new EmailOptions().EffectiveRetryMinutes);
    }

    [Fact]
    public void A_configured_schedule_replaces_the_default_instead_of_being_appended_to_it()
    {
        // The configuration binder adds configured items to a list that already has values; this is why the option has no default list.
        var bound = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Email:RetryMinutes:0"] = "0", ["Email:RetryMinutes:1"] = "2" })
            .Build().GetSection(EmailOptions.Section).Get<EmailOptions>()!;
        Assert.Equal(new[] { 0, 2 }, bound.EffectiveRetryMinutes);
        Assert.Equal(TimeSpan.Zero, bound.RetryDelay(1));
        Assert.Equal(TimeSpan.FromMinutes(2), bound.RetryDelay(2));
        var none = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build().GetSection(EmailOptions.Section).Get<EmailOptions>();
        Assert.Null(none);   // an absent section binds to nothing: the host then uses new EmailOptions() with the defaults
    }

    // ---- composing ------------------------------------------------------------------------------------------------------

    private static NotificationMessage Msg(string title = "Tolerance request TOL-00003 needs a decision", string? body = "apclerk asked: price rise", string? link = "/tolerance-requests") =>
        new("approval", title, body, link, "s", true);

    [Fact]
    public void An_email_has_a_prefixed_subject_a_greeting_the_message_and_an_absolute_link()
    {
        var mail = EmailComposer.Compose(Msg(), "admin@example.com", "admin", On());
        Assert.Equal("[GTEK ERP] Tolerance request TOL-00003 needs a decision", mail.Subject);
        Assert.StartsWith("Hello admin,", mail.Text);
        Assert.Contains("apclerk asked: price rise", mail.Text);
        Assert.Contains("https://erp.example.com/tolerance-requests", mail.Text);
        Assert.Contains("<a href=\"https://erp.example.com/tolerance-requests\">", mail.Html);
        Assert.Equal(("admin@example.com", "admin"), (mail.ToAddress, mail.ToName));
    }

    [Fact]
    public void Message_content_cannot_inject_html_or_extra_headers()
    {
        var mail = EmailComposer.Compose(Msg("Bad\r\nBcc: evil@example.com <b>x</b>", "<script>alert(1)</script> & more"), "a@example.com", "<i>name</i>", On());
        Assert.DoesNotContain('\r', mail.Subject);
        Assert.DoesNotContain('\n', mail.Subject);
        Assert.DoesNotContain("<script>", mail.Html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; &amp; more", mail.Html);
        Assert.DoesNotContain("<i>name</i>", mail.Html);
    }

    [Theory]
    [InlineData("/tolerance-requests", "https://erp.example.com/tolerance-requests")]
    [InlineData("/purchase-orders/abc", "https://erp.example.com/purchase-orders/abc")]
    [InlineData("https://evil.example/phish", null)]      // never put an arbitrary URL into an email
    [InlineData("//evil.example/phish", null)]            // protocol-relative
    [InlineData("javascript:alert(1)", null)]
    [InlineData("tolerance-requests", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Only_in_app_paths_become_links(string? link, string? expected) =>
        Assert.Equal(expected, EmailComposer.AbsoluteLink(link, "https://erp.example.com"));

    [Fact]
    public void A_trailing_slash_on_the_base_url_does_not_double_up() =>
        Assert.Equal("https://erp.example.com/x", EmailComposer.AbsoluteLink("/x", "https://erp.example.com/"));

    [Fact]
    public void Without_a_link_or_body_the_email_still_reads_properly()
    {
        var mail = EmailComposer.Compose(Msg(body: null, link: null), "a@example.com", null, On());
        Assert.StartsWith("Hello,", mail.Text);
        Assert.DoesNotContain("Open it here", mail.Text);
        Assert.DoesNotContain("<a href", mail.Html);
    }

    // ---- the outbox message -----------------------------------------------------------------------------------------------

    private static EmailMessage NewMail() => EmailMessage.Create(new CompanyId(Guid.NewGuid()), new UserId(Guid.NewGuid()), "a@example.com", "A", "Subject", "text", "<p>html</p>", Now);

    [Fact]
    public void A_new_message_is_pending_and_due_immediately()
    {
        var m = NewMail();
        Assert.Equal((EmailStatus.Pending, 0, Now), (m.Status, m.Attempts, m.NextAttemptAt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-at-sign")]
    public void A_recipient_address_is_required(string address) =>
        Assert.Throws<DomainException>(() => EmailMessage.Create(null, null, address, null, "S", "t", "h", Now));

    [Fact]
    public void A_subject_is_required() =>
        Assert.Throws<DomainException>(() => EmailMessage.Create(null, null, "a@example.com", null, "  ", "t", "h", Now));

    [Fact]
    public void A_transient_failure_schedules_a_retry_and_keeps_the_message_pending()
    {
        var m = NewMail();
        m.MarkFailed("connection refused", permanent: false, Now, maxAttempts: 5, retryAfter: TimeSpan.FromMinutes(5));
        Assert.Equal((EmailStatus.Pending, 1, Now.AddMinutes(5), "connection refused"), (m.Status, m.Attempts, m.NextAttemptAt, m.LastError));
    }

    [Fact]
    public void A_permanent_failure_or_the_last_attempt_gives_up()
    {
        var a = NewMail(); a.MarkFailed("550 no such user", permanent: true, Now, 5, TimeSpan.FromMinutes(1));
        Assert.Equal(EmailStatus.Failed, a.Status);
        var b = NewMail(); b.MarkFailed("x", false, Now, maxAttempts: 1, retryAfter: TimeSpan.FromMinutes(1));
        Assert.Equal(EmailStatus.Failed, b.Status);
        var c = NewMail(); c.MarkFailed("x", false, Now, maxAttempts: 5, retryAfter: null);
        Assert.Equal(EmailStatus.Failed, c.Status);
    }

    [Fact]
    public void Sending_records_the_time_clears_the_error_and_counts_the_attempt()
    {
        var m = NewMail();
        m.MarkFailed("temporary", false, Now, 5, TimeSpan.FromMinutes(1));
        m.MarkSent(Now.AddMinutes(2));
        Assert.Equal((EmailStatus.Sent, 2, (DateTimeOffset?)Now.AddMinutes(2), (string?)null), (m.Status, m.Attempts, m.SentAt, m.LastError));
    }

    [Fact]
    public void Long_errors_and_subjects_are_cut_to_fit()
    {
        var m = EmailMessage.Create(null, null, "a@example.com", null, new string('s', 400), "t", "h", Now);
        Assert.Equal(300, m.Subject.Length);
        m.MarkFailed(new string('e', 2000), false, Now, 5, TimeSpan.FromMinutes(1));
        Assert.Equal(1000, m.LastError!.Length);
    }

    // ---- delivery -----------------------------------------------------------------------------------------------------------

    private sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }

    private sealed class FakeOutbox(params EmailMessage[] due) : IEmailOutboxStore
    {
        public int Saves { get; private set; }
        public (DateTimeOffset Sent, DateTimeOffset Failed)? Purged { get; private set; }
        public (DateTimeOffset Now, int Batch, DateTimeOffset Lease)? Claimed { get; private set; }
        public Task AddRangeAsync(IEnumerable<EmailMessage> m, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<EmailMessage>> ClaimDueAsync(DateTimeOffset now, int batch, DateTimeOffset leaseUntil, Guid token, CancellationToken ct)
        {
            Claimed = (now, batch, leaseUntil);
            return Task.FromResult<IReadOnlyList<EmailMessage>>(due);
        }
        public Task<int> PurgeAsync(DateTimeOffset sentBefore, DateTimeOffset failedBefore, CancellationToken ct) { Purged = (sentBefore, failedBefore); return Task.FromResult(2); }
        public Task<EmailOutboxStatus> StatusAsync(CompanyId c, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct) { Saves++; return Task.CompletedTask; }
    }

    private sealed class FakeSender(Func<OutgoingEmail, Exception?> behave) : IEmailSender
    {
        public List<OutgoingEmail> Sent { get; } = [];
        public Task SendAsync(OutgoingEmail email, CancellationToken ct)
        {
            if (behave(email) is { } failure) throw failure;
            Sent.Add(email);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Due_messages_are_sent_and_marked_sent_and_each_is_saved_on_its_own()
    {
        var a = NewMail(); var b = NewMail();
        var outbox = new FakeOutbox(a, b);
        var sender = new FakeSender(_ => null);
        var run = await new EmailDelivery(outbox, sender, On(), new FixedClock(Now)).RunOnceAsync(default);
        Assert.Equal((2, 0, 0), (run.Sent, run.Retrying, run.GaveUp));
        Assert.Equal(new[] { EmailStatus.Sent, EmailStatus.Sent }, new[] { a.Status, b.Status });
        Assert.Equal(2, outbox.Saves);                                   // saved per message, so a crash cannot resend a sent one
        Assert.Equal(("a@example.com", "Subject"), (sender.Sent[0].ToAddress, sender.Sent[0].Subject));
    }

    [Fact]
    public async Task The_claim_uses_the_batch_size_and_a_lease_and_old_messages_are_purged_afterwards()
    {
        var outbox = new FakeOutbox();
        var options = On(); options.BatchSize = 7; options.LeaseMinutes = 9; options.KeepSentDays = 3; options.KeepFailedDays = 40;
        var run = await new EmailDelivery(outbox, new FakeSender(_ => null), options, new FixedClock(Now)).RunOnceAsync(default);
        Assert.Equal((Now, 7, Now.AddMinutes(9)), outbox.Claimed);
        Assert.Equal((Now.AddDays(-3), Now.AddDays(-40)), outbox.Purged);
        Assert.Equal(2, run.Purged);
    }

    [Fact]
    public async Task A_transient_failure_is_retried_later_with_the_configured_delay_and_does_not_stop_the_batch()
    {
        var bad = NewMail(); var good = EmailMessage.Create(null, null, "ok@example.com", null, "Other", "t", "h", Now);
        var sender = new FakeSender(e => e.ToAddress == "a@example.com" ? new EmailDeliveryException("connection refused", permanent: false) : null);
        var run = await new EmailDelivery(new FakeOutbox(bad, good), sender, On(), new FixedClock(Now)).RunOnceAsync(default);
        Assert.Equal((1, 1, 0), (run.Sent, run.Retrying, run.GaveUp));
        Assert.Equal((EmailStatus.Pending, 1, Now.AddMinutes(1), "connection refused"), (bad.Status, bad.Attempts, bad.NextAttemptAt, bad.LastError));
        Assert.Equal(EmailStatus.Sent, good.Status);
    }

    [Fact]
    public async Task A_rejected_address_is_given_up_at_once()
    {
        var m = NewMail();
        var run = await new EmailDelivery(new FakeOutbox(m), new FakeSender(_ => new EmailDeliveryException("550 no such user", permanent: true)), On(), new FixedClock(Now)).RunOnceAsync(default);
        Assert.Equal((0, 0, 1), (run.Sent, run.Retrying, run.GaveUp));
        Assert.Equal((EmailStatus.Failed, "550 no such user"), (m.Status, m.LastError));
    }

    [Fact]
    public async Task Repeated_transient_failures_follow_the_back_off_then_give_up()
    {
        var m = NewMail();
        var options = On(); options.MaxAttempts = 3; options.RetryMinutes = [1, 10];
        var sender = new FakeSender(_ => new EmailDeliveryException("down", permanent: false));
        await new EmailDelivery(new FakeOutbox(m), sender, options, new FixedClock(Now)).RunOnceAsync(default);
        Assert.Equal((EmailStatus.Pending, Now.AddMinutes(1)), (m.Status, m.NextAttemptAt));
        await new EmailDelivery(new FakeOutbox(m), sender, options, new FixedClock(Now.AddMinutes(1))).RunOnceAsync(default);
        Assert.Equal((EmailStatus.Pending, Now.AddMinutes(11)), (m.Status, m.NextAttemptAt));
        await new EmailDelivery(new FakeOutbox(m), sender, options, new FixedClock(Now.AddMinutes(11))).RunOnceAsync(default);
        Assert.Equal((EmailStatus.Failed, 3), (m.Status, m.Attempts));      // third failure of three: stop
    }

    [Fact]
    public async Task An_unexpected_error_from_the_sender_counts_as_a_transient_failure()
    {
        var m = NewMail();
        await new EmailDelivery(new FakeOutbox(m), new FakeSender(_ => new InvalidOperationException("boom")), On(), new FixedClock(Now)).RunOnceAsync(default);
        Assert.Equal((EmailStatus.Pending, "boom"), (m.Status, m.LastError));
    }

    [Fact]
    public async Task Shutting_down_stops_the_run_without_marking_the_message_failed()
    {
        var m = NewMail();
        using var cts = new CancellationTokenSource();
        var sender = new FakeSender(_ => { cts.Cancel(); return new OperationCanceledException(cts.Token); });
        await Assert.ThrowsAsync<OperationCanceledException>(() => new EmailDelivery(new FakeOutbox(m), sender, On(), new FixedClock(Now)).RunOnceAsync(cts.Token));
        Assert.Equal((EmailStatus.Pending, 0), (m.Status, m.Attempts));
    }

    // ---- preferences -------------------------------------------------------------------------------------------------------

    [Fact]
    public void New_users_have_email_notifications_on_and_can_turn_them_off()
    {
        var u = User.Create("someone", "someone@example.com", Now);
        Assert.True(u.EmailNotifications);
        u.SetEmailNotifications(false);
        Assert.False(u.EmailNotifications);
    }
}
