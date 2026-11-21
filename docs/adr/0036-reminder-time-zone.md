# ADR 0036: A time zone for the reminder schedule

## Status

Accepted. Replaces the UTC-only schedule of ADR 0029 (`HourUtc` is now `Hour`).

## Context

Reminders were sent "from 08:00 UTC" with "once per day" counted in UTC days. For a company in Colombo (UTC+5:30) that is 13:30, and a message
sent late on one local day could count as the same day as the next morning's, or not.

## Decision

- `Reminders:TimeZone` (an IANA id such as `Asia/Colombo`; `UTC` by default) and `Reminders:Hour` (0 to 23, default 8) replace `HourUtc`.
  A reminder is due when the **local hour** in that zone is at least `Hour`, and "already sent in this period" compares **local calendar dates**
  (the last-sent time converted to the zone), so it follows the company's day, daylight saving included.
- An unknown time zone **stops start-up** with the rest of the reminder validation errors. If a bad value ever reached the schedule it falls
  back to UTC rather than throw.
- **Only the schedule is zoned.** What counts as overdue, ended or low is still judged on UTC dates, like the invoice and bill lists and every
  other business date in the application, so a reminder can never disagree with the screens it links to.

## Consequences

- One time zone for the installation; companies in different zones need separate installations or accept one schedule.
- The send time is the first timer tick (`IntervalMinutes`, an hour by default) at or after the configured hour, in that zone.
- Verified by unit tests across UTC, `Asia/Colombo` (a half-hour offset) and `America/New_York` (daylight saving), including a case where the
  UTC date and the local date differ.
- Not covered: per-company or per-user time zones and send times, and making business dates themselves time-zone aware.
