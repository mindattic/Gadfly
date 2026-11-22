# Connecting your LMS (LTI 1.3)

Connect Canvas, Moodle, Blackboard Learn or D2L Brightspace to Gadfly and:

- instructors add a Gadfly exam to a course from the LMS's own content picker;
- students open the exam straight from their course, with no separate Gadfly password;
- everyone in the course can be brought into your organization in one step, instructors as Instructors and learners
  as Students;
- each grade goes back to the LMS gradebook once you release it in Gadfly.

Gadfly speaks the standard **LTI 1.3 / LTI Advantage** (Core, Deep Linking, Names and Role Provisioning, Assignment
and Grade Services). Only your organization's owners and admins can set it up, on **Organization → LMS connection**
(`/org/lti`). Every change there is recorded in the audit log.

## How it fits together

Set-up has two halves, and each side needs a few values from the other:

1. **In your LMS**, register Gadfly as an LTI 1.3 tool using **Gadfly's addresses** (shown at the top of the LMS
   connection page, with copy buttons). The LMS then gives you a **client ID** and a **deployment ID**.
2. **In Gadfly**, add a connection with the LMS's values: its issuer, its login, token and key-set addresses, and the
   client ID and deployment ID from step 1. The **Fill in** button enters everything but the two IDs for the LMS you
   choose; check the values against what your LMS shows.

### Gadfly's addresses

Replace `https://gadfly.example.com` with your Gadfly address (the page shows the exact ones):

| What the LMS calls it | Value |
|---|---|
| Redirect URI, Tool URL, Target Link URI, Content Selection / Deep Linking URL | `https://gadfly.example.com/lti/launch` |
| OIDC Login / Initiate login / Login Initiation URL | `https://gadfly.example.com/lti/login` |
| Public JWK URL / Keyset URL / Tool JWKS URL | `https://gadfly.example.com/lti/jwks.json` |
| Domain | `gadfly.example.com` |

Signing is RS256 with Gadfly's public key set. There is no shared secret to copy or rotate. When MindAttic rotates
Gadfly's signing key, the new key appears in the key set at once and the old one stays there for 7 days, so your LMS
picks up the change by itself: use the key-set URL (not a pasted key) and there is nothing to update.

## Canvas

1. As an account admin: **Admin → (your account) → Developer Keys → + Developer Key → + LTI Key**, *Manual Entry*.
2. **Redirect URIs**: the launch URL. **Title**: Gadfly Exams. **Target Link URI**: the launch URL.
   **OpenID Connect Initiation Url**: the login URL. **JWK Method**: Public JWK URL, the key-set URL.
3. **LTI Advantage Services**: turn on "Can create and view assignment data in the gradebook associated with the
   tool", "Can create and update submission results for assignments associated with the tool" and "Can retrieve user
   data associated with the context the tool is installed in".
4. **Placements**: *Assignment Selection* and *Link Selection*, each with Message Type *LtiDeepLinkingRequest* and
   Target Link URI the launch URL.
5. **Privacy level**: *Public*, so Gadfly receives names and emails. Save, switch the key **ON**, and copy its
   **Client ID** (the number in the Details column).
6. **Admin → Settings → Apps → View App Configurations → + App**, *By Client ID*, paste it, Install. On the app's gear
   menu choose **Deployment Id** and copy it.
7. In Gadfly, choose *Canvas*, press **Fill in for Canvas**, paste the Client ID and Deployment ID, and add the
   connection. Canvas's values are: issuer `https://canvas.instructure.com`, login
   `https://sso.canvaslms.com/api/lti/authorize_redirect`, token `https://sso.canvaslms.com/login/oauth2/token`, key
   set `https://sso.canvaslms.com/api/lti/security/jwks`. Canvas test and beta use the issuer
   `https://canvas.test.instructure.com` or `https://canvas.beta.instructure.com`; a self-hosted Canvas has its own.

Instructors then create an assignment with Submission type **External Tool**, press **Find**, choose Gadfly Exams and
pick the exam. Tick **Load This Tool In A New Tab**.

## Moodle

1. As a site administrator: **Site administration → Plugins → Activity modules → External tool → Manage tools →
   configure a tool manually**.
2. **Tool name**: Gadfly Exams. **Tool URL**: the launch URL. **LTI version**: LTI 1.3. **Public key type**: Keyset
   URL, **Public keyset**: the key-set URL. **Initiate login URL**: the login URL. **Redirection URI(s)**: the launch URL.
3. **Tool configuration usage**: show in activity chooser and as a preconfigured tool. **Default launch container**:
   New window. Tick **Supports Deep Linking (Content-Item Message)**; **Content Selection URL**: the launch URL.
4. **Services**: Assignment and Grade Services, "Use this service for grade sync and column management"; Names and
   Role Provisioning, "Use this service to retrieve members' information as per privacy settings".
5. **Privacy**: share the launcher's name, Always; share the launcher's email, Always; accept grades from the tool,
   Always. Save.
6. On the tool's card, **View configuration details** shows the Platform ID, Client ID and Deployment ID.
7. In Gadfly, choose *Moodle*, type your Moodle address, press **Fill in for Moodle**, paste the two IDs and add the
   connection. Moodle's values are: issuer = your Moodle address, login `/mod/lti/auth.php`, token
   `/mod/lti/token.php`, key set `/mod/lti/certs.php` (all on your Moodle address).

Teachers then add an **External tool** activity, choose Gadfly Exams, press **Select content** and pick the exam.

## Blackboard Learn

1. On the Anthology developer portal (developer.blackboard.com): **My Apps → + → Manually register your
   application**. **Domain(s)**: your Gadfly domain. **Login Initiation URL**: the login URL. **Tool Redirect URL(s)**:
   the launch URL. **Tool JWKS URL**: the key-set URL. **Signing algorithm**: RS256. Register, and copy the
   **Application ID** (the client ID).
2. In Learn: **Administrator Panel → Integrations → LTI Tool Providers → Register LTI 1.3/Advantage Tool**, paste the
   client ID, Submit, and copy the **Deployment ID**.
3. Set **Tool Status** to Approved; send role in course, name and email; allow grade service access and membership
   service access.
4. **Manage Placements → Create Placement**: type *Deep Linking content tool*, Target Link URI the launch URL, and
   tick *Launch in new window*.
5. In Gadfly, paste the client ID, choose *Blackboard Learn*, press **Fill in for Blackboard Learn**, add the
   Deployment ID and add the connection. Blackboard's values are: issuer `https://blackboard.com`, login
   `https://developer.blackboard.com/api/v1/gateway/oidcauth`, token
   `https://developer.blackboard.com/api/v1/gateway/oauth2/jwttoken`, key set
   `https://developer.blackboard.com/api/v1/management/applications/<client ID>/jwks.json`.

Instructors then add Gadfly Exams from the Content Market (Ultra) or Build Content (Original) and pick the exam.

## D2L Brightspace

1. **Admin Tools → Manage Extensibility → LTI Advantage → Register Tool**, type *Standard*. **Domain**: your Gadfly
   address. **Redirect URLs**: the launch URL. **OpenID Connect Login URL**: the login URL. **Target Link URI**: the
   launch URL. **Keyset URL**: the key-set URL. **Extensions**: Assignment and Grade Services, Deep Linking, Names and
   Role Provisioning Services. Register, and copy the **Client Id**.
2. **View Deployments → New Deployment**: the same extensions; send org unit and user information (name, email) and
   the classlist; choose the org units. Copy the **Deployment Id**.
3. **View Links → New Link**: type *Deep Linking Quicklink*, URL the launch URL.
4. In Gadfly, choose *D2L Brightspace*, type your Brightspace address, press **Fill in for D2L Brightspace**, paste the
   two IDs and add the connection. Brightspace's values are: issuer = your Brightspace address, login
   `/d2l/lti/authenticate` and key set `/d2l/.well-known/jwks` on that address, token
   `https://auth.brightspace.com/core/connect/token`, and **token audience** `https://api.brightspace.com/auth/token`.
   Check the issuer against the one the registration screen shows.

Instructors then use **Existing Activities → Gadfly Exams** in a course, pick the exam, and set it to open as an
external resource (a new window).

## What students and instructors see

- **Adding an exam.** An instructor uses the LMS's content picker and sees Gadfly's list of the organization's
  published exams (only their own when your organization limits instructors to the exams they built). Choosing one
  adds it to the course and, on platforms that support it, creates its gradebook column.
- **A student opening it.** Gadfly checks the launch, finds or creates the student's account and membership, assigns
  them the exam, signs them in and opens the exam's get-ready page. If the LMS shows Gadfly inside its own page, the
  student gets an **Open the exam in a new window** button instead, because the exam needs the camera and microphone.
  The button works once, for two minutes, on whichever Gadfly server answers (it is checked in the database).
- **A browser that blocks cookies inside the LMS page.** Some browsers block every cookie for a site shown inside
  another page, including the one Gadfly uses to check that a launch started in the same browser. Gadfly still checks
  the launch, then shows **Open in a new window** instead of an error: it starts the same launch again in its own
  window, where the cookie works. If that page keeps coming back in a normal window, the browser is blocking cookies
  for Gadfly altogether.
- **An instructor opening it.** They are told which exam the placement is linked to. Grading and editing happen in
  Gadfly itself, with the usual multi-factor sign-in: an LMS launch never counts as that.

## People, roles and seats

- A person is matched by the LMS's own ID for them, per connection. The first time they arrive (by opening an exam,
  or through a roster sync) Gadfly creates their account with the name and email the LMS sent.
- **Someone who already has a Gadfly account.** When the LMS sends the email address of exactly one existing member of
  your organization, Gadfly creates no second account, and does not link the two on the email alone either (an LMS can
  send any address). Until it is confirmed, the person can't open the exam through the LMS. It is confirmed either way:
  - **by the person**, when their Gadfly account's email address is verified: the launch page offers **Email me a link
    to connect it**. The link goes to that account's address, works once within 24 hours, and only in the browser that
    asked for it, so knowing someone's address is not enough;
  - **by an admin**, under **Waiting to be linked** on the LMS connection page: **Link** when you know they are the same
    person, or **Keep separate**, which gives the LMS person an account of their own the next time they open an exam
    or the roster syncs.

  Each step is in the audit log (`lti.link-requested`, `lti.link-email-sent`, `lti.identity-linked`,
  `lti.identity-kept-separate`). An account that is already linked to another LMS person on the same connection is never
  offered.
- **Instructor**, **Teaching Assistant**, **Content Developer** and course **Administrator** become **Instructor**;
  **Learner** becomes **Student**. Anyone else (a mentor or observer, for example) is turned away with an explanation.
- The LMS can move a student up to instructor. It never moves anyone down, never makes anyone an owner or admin, and
  never brings back a member you removed or an account you turned off.
- New members take a seat under your plan, the same as an invitation. When seats run out, the person is told to ask
  the organization's owner.
- LMS-created instructors who want the full Gadfly app use **Forgot password** with the email the LMS gave, then set
  up multi-factor sign-in.

## Placements, rosters and grades

The **Placements** table lists each exam added to a course, once it has been opened from the course for the first
time.

- **Sync roster** reads the course's member list from the LMS and gives each active member a Gadfly account in your
  organization with the role above. Use it when you want students visible (for accommodations, for example) before
  they open the exam.
- **Scheduled roster refresh.** Each connection also refreshes its courses' rosters on its own, every 24 hours by
  default. Change it with **Refresh course rosters every … hours** on the connection (1 to 720 hours), or set 0 to sync
  only when you press the button. The refresh runs in the background job queue; a refresh the LMS refused shows in the
  Roster column with its reason, and is tried again at the next interval.
- **Grades** go back automatically within a few minutes of a result being released to the student in Gadfly: the
  final percentage of their latest attempt, out of 100. An amended grade is sent again. Integrity notes never are.
  If the LMS offered no gradebook service for the placement, the table says so and no grade is sent.
- **A grade the LMS refuses** (or doesn't answer) is tried again two minutes later, then waiting twice as long each
  time, up to 8 tries (about four hours). The placement shows how many are retrying; **Grades the LMS hasn't accepted**
  lists each one with the last reason. After the last try it is marked **failed** (audited as `lti.grade-failed`) and
  waits for you: fix the cause, then press **Retry now** (or **Retry grades** on the placement). A grade that changes
  is sent afresh with new tries.

## Turning a connection off

**Turn off** stops all launches and grade sending through a connection but keeps its placements and accounts; turn it
back on to resume (grades owed in the meantime are then sent). A connection that was never used can be removed.

## When something goes wrong

| Message | What to check |
|---|---|
| "This platform is not connected to Gadfly" | The issuer and client ID in Gadfly match the LMS's exactly, and the connection is on. |
| "The launch's deployment id does not match" | Copy the deployment ID again from the LMS (each installation has its own). |
| "signed with a key the platform's key set does not list" or "could not read the platform's key set" | The key-set (JWKS) URL. |
| "This browser did not start this launch" | The cookie came back different: open the exam from the LMS again. |
| "Open Gadfly in a new window" | The browser blocked Gadfly's cookie inside the LMS page: press the button, or set the link to open in a new window. |
| "You may already have a Gadfly account" | The LMS sent an existing member's email: link or keep them separate under **Waiting to be linked**, or the person confirms by email. |
| "This assignment isn't linked to a Gadfly exam yet" | Add the exam through the content picker (deep linking), not as a plain link. |
| Grades don't arrive | The connection is on, the gradebook services were enabled in the LMS, and the result is released (published) in Gadfly. **Grades the LMS hasn't accepted** gives the LMS's reason. |

## What isn't covered yet

- One grade per placement (the final percentage), not one per learning objective; the full report stays in Gadfly.
- An LMS sign-in is not listed under "sign out everywhere"; it ends at the usual idle and absolute time-outs.
- LTI 1.1, and the LTI "platform storage" API (browsers that block every cookie inside a frame get the new-window
  button instead).
