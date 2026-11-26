# System status and jobs

**System status** (`/admin/status`, under Organization in the admin menu) is for an organization's owners and admins
and for the platform admin. It reads what the app has already recorded and calls no AI or speech provider, so refreshing
it costs nothing. Press **Refresh** to read it again.

## Checks

A table of checks, each **OK**, **Needs a look** or **Failing**:

| Check | Fails when | Needs a look when |
|---|---|---|
| Database | it does not answer | |
| Database migrations | some are not applied (the database is behind the app) | the history could not be read |
| AI panel | | it is the mock panel outside development, or providers are failing |
| Voice | | voice is off, simulated outside development, or the latest speech call failed |
| Background jobs | | a job anywhere on the platform failed in the last 24 hours, or one has waited more than 15 minutes to start |
| Webhook deliveries | | a delivery was given up in the last 24 hours |
| Errors in the log | | this server logged an error in the last hour |

The same checks answer `GET /healthz/ready` for monitoring: names and states only, no counts or error text, so it is
anonymous. It answers 200 while nothing fails and 503 when something does.

## App, AI and voice

- The app's version and environment.
- The AI panel's mode (live or mock), the `Gadfly:AiMode` setting, the last switch and why that mode is in force. The
  platform admin sees **Switch AI panel to live / mock** here: it asks you to confirm, takes effect without a restart,
  is recorded in the audit log, and leaves exams already in progress in the mode they started in.
- Each AI provider on the panel: its model, whether it is answering or failing, how many calls it made and failed, and
  when it last answered and last failed, since this server started. The platform admin also sees each provider's last
  error.
- Speech: which speech-to-text and text-to-speech providers are in use, and the latest outcome of each kind of speech
  call (the recognizer's start, commit and stop, and the examiner's voice) in the last 24 hours.

## Database and background jobs

Whether the database answers, how many migrations are applied and the latest one, and any not yet applied. The
platform admin also sees the database's size.

Your organization's jobs (reading source material, AI grading, webhook deliveries, emails, retention): those waiting or
running, and those that failed or were cancelled in the last 7 days (the same window as the dashboard's *Background
work that failed*, so its link shows the jobs it counted), with their attempts and error. **Jobs failed** above the list
counts the same 7 days. **Retry** runs a
failed or cancelled job again from the start with a fresh set of attempts; **Cancel** stops a waiting job, or a running
one at its next progress report. Both are recorded in the audit log (`admin.job.retried`, `admin.job.cancelled`).

## Webhooks

Active endpoints, deliveries waiting to send or to retry, delivered and given up in the last 24 hours, and each delivery
that failed or was given up, with its response code. Endpoints themselves are managed on **Webhooks** (`/org/webhooks`).

## Storage

Your organization's source documents and their size, source passages, exam sessions, the size of their transcript text
and the number of audit entries. No exam audio or video is stored.

## Time to the next question

How long each kind of outside call on a live exam took in the last 24 hours (the panel's steps and the examiner's voice),
with averages, the worst case and how often it succeeded.

## Recent errors (platform admin)

The last 50 errors this server logged, kept in memory since it started. Only the platform admin sees this section,
because it covers every organization. The full log is in the App Service log stream.
