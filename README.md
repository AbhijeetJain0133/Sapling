# Sapling

A career-readiness app for college students in Madhya Pradesh. It shows each student how well they fit the career paths their course leads to, what skills they are missing, and a week-by-week plan to close those gaps.

Sapling runs as a Blazor Server web app and as a .NET MAUI app for Android and Windows. Both share the same pages and components.

## Features

- **Onboarding.** A short setup covering the student's details (college search with state and city filled in automatically, course, branch, graduation year, CGPA, backlogs), their skills, and an interest quiz.
- **Career & skills.** One page for the student's career. Until they choose, it lists all 104 roles (built from the O\*NET database, U.S. Department of Labor), filterable to their course, searchable, and sortable by skill match or job outlook; each role opens in full with a "Make this my target" button. Once chosen, the page shows the target in full (description, fit, reasons and things to weigh, day-to-day tasks, preparation, technologies, related careers) together with the student's skills: what's missing ranked by O\*NET importance with rough time estimates, what's already covered, and their own skill list. "Change career" brings the list back. Fit is 75% skills (the share of the role's requirements the student has, more important ones counting for more), 15% whether their course is a usual route into it, and 10% how their interest quiz matches the role. Skill lists are trusted as entered. Salary and Indian demand data are not included yet.
- **Roadmap.** For each skill the target role asks for, a free course with a link and checkpoints to tick off: 48 NPTEL courses from the IITs and IISc, and 10 Microsoft Learn learning paths. Where a skill has both, the student picks which to follow, and only that one counts towards progress. Steps can be sorted by missing first, importance, or skills already acquired. Finishing a course that teaches a skill directly adds it to the student's skills; "builds the foundations" courses teach the subject beneath a tool, so the student confirms the tool themselves. Skills neither platform teaches are left out.
- **Opportunities.** Internships, jobs and campus drives ranked by fit. Roles the student isn't ready for still appear, with a list of what's missing.
- **Resume.** Three ways in: upload a PDF, build one step by step (prefilled from the profile), or describe yourself and let the AI draft the sections. An uploaded resume gets an AI score with five sub-scores and concrete suggestions, and is copied into one of two ATS-safe LaTeX templates (single or two column). The editor shows the LaTeX beside a live PDF preview, applies any suggestion with one click, takes free-text instructions ("shorten the summary"), keeps one-step undo of AI changes, autosaves, and exports PDF or `.tex`. The AI only rephrases facts the student gave; it never invents experience. Web only; the mobile app lists saved resumes.
- **Mock interview.** A spoken interview with an animated AI interviewer, in a full-screen meeting room with an optional camera self-view that stays on the student's device. The student picks a role (their target career by default), a length (short, medium or long; about 10, 20 or 30 minutes), adds optional instructions, and can upload a PDF resume for the interviewer to ask about. The interviewer speaks each question, listens to the answer, privately scores it, and picks the next question from how the interview is going, through an introduction, background, technical, behavioural and closing phase. It decides when to finish, a little before or after the planned length. Afterwards a report shows scores by area, where the student got stuck, topics to work on, question-by-question feedback, and delivery measured from the microphone: time to start answering, long pauses, pace, filler words and a confidence level. Every past interview's report and transcript stays on the page. Answers are spoken only. Before joining, the student checks their microphone (with a level meter), speaker and camera, and can switch devices during the interview as in Google Meet. Speech recognition uses the browser, so the interview needs Chrome or Edge on the web app. The mobile app shows past reports.
- **Government track.** MPPSC, MPESB, SSC, Railways and Banking exams, each with an eligibility check.
- **Community.** A private feed for each college, where it posts workshops, events, opportunities and announcements. Students of that college can upvote posts and comment in threads, while only the college itself can post. Posts carry a verified badge.
- **Home dashboard.** The student's target role and how well they fit it, banners, recently visited features, roadmap progress and the latest community posts.
- **Profile.** Editable details and a profile picture.
- **Accounts.**
  - Sign-up with email verification: the student enters an email, types in a 6-digit code sent to it, then chooses a password.
  - Forgot password, with the same code check.
  - Google sign-in.
  - A demo account with sample data.
- **Themes.** Light and dark.

## Projects

| Project | What it contains |
|---|---|
| `Sapling.Shared` | Razor pages, components, styles and service contracts used by both apps |
| `Sapling.Web` | Blazor Server host: EF Core with SQLite, ASP.NET Identity, the HTTP API, the AI client, and demo data seeding |
| `Sapling` | .NET MAUI Blazor Hybrid app for Android and Windows, which talks to `Sapling.Web` over HTTP |

## Running it

You need the .NET 10 SDK, plus the MAUI workload if you want the mobile app.

```bash
dotnet run --project Sapling.Web/Sapling.Web.csproj
```

The web app runs at `http://localhost:5250`. On first start it creates `sapling.db` and seeds demo data. To try it straight away, sign in with **demo@sapling.app** / **Sapling@2026**.

To run the mobile app, start `Sapling.Web` first, then deploy the `Sapling` project from Visual Studio.
- **Android emulator:** reaches the server at `10.0.2.2`, with no changes needed.
- **Physical phone:** set `DevMachineHost` in `Sapling/Services/HttpServices.cs` to your PC's Wi-Fi IP address, and allow inbound TCP port 5250 through Windows Firewall.

## Updating career data

The role catalogue lives in `tools/careers/catalogue.json`. It lists which O\*NET occupations to include, their Indian job titles, and which courses and branches lead to each one. To rebuild `Sapling.Web/Data/careers.json` after editing it, run:

```bash
dotnet run tools/careers/import.cs
```

The same run fetches the NPTEL courses and Microsoft Learn paths listed in the catalogue's `nptel` and `microsoftLearn` maps (details and lecture lists, cached locally) and turns their lectures into checkpoints. The importer downloads the O\*NET CSV files the first time, then prints any in-demand technologies that don't yet have a matching Sapling skill. The web app loads the new data the next time it starts. O\*NET data is used under CC BY 4.0, and the app credits it on every role page.

## Configuration

Keep secrets in user secrets, not in `appsettings.json`:

```bash
dotnet user-secrets set "<key>" "<value>" --project Sapling.Web
```

| Key | Used for | If not set |
|---|---|---|
| `Ai:HuggingFace:ApiKey` (plus optional `Ai:HuggingFace:Model`, default `openai/gpt-oss-120b`) | The mock interviewer and its report, and resume scoring, drafting and AI edits, through Hugging Face Inference Providers | A scripted offline interviewer and a generic report are used; resumes get sample content and a generic review, and AI edits change nothing |
| `Monitoring:DiscordWebhookUrl` (plus optional `Monitoring:IntervalMinutes`, default 5) | A usage report to a Discord channel on startup and then on each interval: CPU, app and system memory, swap, disk, load, uptime and users online | No reports |
| `Latex:TectonicPath` | Path to the Tectonic executable that builds resume PDFs, when it isn't on `PATH` | `tectonic` on `PATH`; if missing, the resume editor still works but shows no PDF preview or PDF download |
| `Speech:Azure:Key`, `Speech:Azure:Region` (plus optional `Speech:Azure:Voice`, default `en-IN-NeerjaNeural`) | A natural neural voice for the mock interviewer, from Azure AI Speech | The best voice the student's browser has (Microsoft Edge's are the most natural) |
| `Authentication:Google:ClientId`, `Authentication:Google:ClientSecret` | Google sign-in | The Google button shows "Not set up" |
| `Email:Azure:ConnectionString`, `Email:Azure:SenderAddress` | Verification emails through Azure Communication Services | In development, codes are written to the server log |

For the AI key, create a fine-grained Hugging Face token with the "Make calls to Inference Providers" permission. An interview uses roughly 40,000 tokens, about $0.01 on `openai/gpt-oss-120b`. The free Hugging Face tier includes only $0.10 of credit a month, so add credit for regular use. Set `Ai:HuggingFace:Model` to `openai/gpt-oss-20b` to spend about half as much, with weaker follow-up questions.

Resume PDFs are compiled on the server with [Tectonic](https://tectonic-typesetting.github.io), a self-contained LaTeX engine. On Windows, download the `x86_64-pc-windows-msvc.zip` build from its [GitHub releases](https://github.com/tectonic-typesetting/tectonic/releases), unzip it (for example to `C:\Tools\tectonic`), and either add that folder to `PATH` or set `Latex:TectonicPath` to the `.exe`. On Linux or macOS use the install script on its website. The first compile downloads the LaTeX packages the templates use and caches them, so it needs internet and takes up to a minute; later compiles take a second or two. The editor loads CodeMirror from esm.sh and falls back to a plain text box when that is blocked.

Hugging Face has no text-to-speech service, so the natural interviewer voice comes from Azure AI Speech. Its free tier covers 500,000 characters a month, roughly 250 interviews. Create a Speech resource in the Azure portal and copy its key and region.

For Google sign-in, add `http://localhost:5250/signin-google` as an authorized redirect URI on the Google OAuth client. Google rejects private IP addresses, so to use Google sign-in from a phone during development, forward the port over USB with `adb reverse tcp:5250 tcp:5250` and point the app at `localhost`.
