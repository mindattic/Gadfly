# Notes in the eye's reflection, and a room sweep at the start: feasibility

Prepared 2026-11-23 for Gadfly (GAD-T244). The owner asked, one step at a time, whether Gadfly could catch notes taped
to the monitor or held in front of it by reading the screen's reflection in the student's eyes (corneal glint and
reflection analysis from the webcam), and whether a camera sweep of the room at the start of an exam is worth adding.
The ticket asked for feasibility at webcam resolution first. This note answers that. Nothing here changes code.

It builds on note [06](06-integrity-proctoring-gaze-voice.md) (webcam gaze accuracy, proctoring fairness) and
[attention-and-gaze.md](attention-and-gaze.md) Section 3.6 (proctoring costs, Ogletree, the CNIL), and on the
earpiece finding in [docs/guides/integrity-notes.md](../guides/integrity-notes.md) (GAD-T449), which this ticket's
room sweep was also meant to serve.

**Verification levels** follow the ledger ([references/VERIFICATION.md](references/VERIFICATION.md)): **Full**,
**Abstract** (abstract or publisher/index record read; claims stay within it), **Record** (bibliographic record only).
Sources were retrieved 2026-11-23. The optics in Section 2 are worked from textbook values, not from a source, and are
marked as estimates.

---

## 1. Answer

1. **Reading a note from the eye's reflection is not feasible at webcam resolution, by about two orders of
   magnitude.** At the 640x480 Gadfly asks the browser for, the whole monitor shows up in the cornea as a highlight
   about 3 pixels wide. A sticky note on the bezel is under half a pixel, and a line of 12 pt handwriting on it is a
   fiftieth of a pixel. Even a 4K webcam would need letters about a quarter of a metre tall to be read.
2. **Detecting that something is there, without reading it, is not feasible either, in any way a reviewer could
   trust.** A note on the bezel does not cover the screen, so it does not change the screen's highlight; it adds a
   faint sub-pixel smudge beside it that lamps, windows, a second monitor and the iris's own texture swamp. A sheet
   held up between the face and the screen is larger (2 to 12 pixels, depending on the camera), but it is also in
   front of the webcam's own view and draws the eyes away from the examiner, which the eye-contact check already
   notes, with far better signal.
3. **It would also be unfair.** The published attacks that work at webcam resolution use eyeglass lenses, not the
   cornea (Section 3). A reflection check would mostly see glasses-wearers, and differ again by lens coating and
   iris colour, so it would flag one group of students more than another for the same behaviour.
4. **A room sweep at the start is technically trivial but buys little in Gadfly.** No human watches a Gadfly exam
   live, Gadfly stores no frames or video (`docs/PRIVACY.md`), and a note can be put up the moment the sweep ends.
   Recording the sweep would break the privacy design; analysing it on the device would rest on a phone/paper object
   detector whose false-flag rate is unknown. The one US ruling on point found a room scan unreasonable and asked
   whether it was "truly, and uniquely, effective" (Ogletree, below); a sweep that a student can defeat by waiting a
   minute is not.

**Recommendation.** Do not build corneal or eyeglass reflection analysis. Do not add an automated room sweep. If the
owner wants a sweep anyway, the only defensible form is a short, unrecorded, instructor-enabled step on **Strict**
exams that asks the student to show the desk, the edges of the monitor and both ears (the last for GAD-T449), as a
stated expectation rather than a detector: nothing analysed, nothing sent, no note recorded. That decision is filed
as its own ticket (Section 6). Gadfly's defence against notes stays what it is: follow-up questions written from the
student's own previous answer (a note cannot anticipate them), the read-out check, and the eye-contact check.

---

## 2. The optics, worked out

**Estimates, from textbook values.** The front of the cornea is close to a sphere of radius about 7.8 mm, so it acts
as a convex mirror of focal length about 3.9 mm. An object of size *h* at distance *d* from the eye forms a virtual
image of size about *h* x 3.9 / (*d* + 3.9). The visible iris is about 11.7 mm across. Only about 2.5% of the light
reaching the cornea is reflected (Fresnel, refractive index about 1.376).

Gadfly's camera is opened at `{ width: { ideal: 640 }, height: { ideal: 480 } }` (`wwwroot/js/voice-room.js`,
`wwwroot/js/system-check.js`). Assuming a typical 65 degree horizontal field of view and a face 600 mm from the camera,
the frame covers about 765 mm of the scene at that distance:

| Camera width | mm per pixel at the eye | Iris | Monitor highlight (24", 600 mm) | Sticky note on bezel (76 mm) | A4 sheet held at 350 mm | 12 pt letter (2.9 mm) on the note |
|---|---|---|---|---|---|---|
| 640 (Gadfly today) | 1.19 | 10 px | 2.9 px | 0.4 px | 1.9 px | 0.016 px |
| 1280 (720p) | 0.60 | 20 px | 5.7 px | 0.8 px | 3.9 px | 0.03 px |
| 1920 (1080p) | 0.40 | 29 px | 8.5 px | 1.2 px | 5.8 px | 0.05 px |
| 3840 (4K) | 0.20 | 58 px | 17 px | 2.5 px | 12 px | 0.1 px |

Image sizes in the cornea: monitor 3.4 mm, sticky note 0.49 mm, A4 sheet 2.3 mm, letter 0.019 mm. A laptop screen
(310 mm wide at 500 mm) gives 2.4 mm, a little smaller than the monitor.

What the table leaves out makes it worse, not better:

- **Pixels are not resolution.** Webcam lenses, fixed focus, low-light noise and the sensor's colour mosaic mean the
  real detail is coarser than the pixel count, and resolving a pattern takes at least two pixels per line pair.
- **Reading needs roughly 8 pixels per letter height.** At 4K that is a 1.6 mm image, which at 600 mm means letters
  about 250 mm tall. At Gadfly's 640 width it means letters well over a metre tall.
- **Contrast is low.** The screen is bright enough that its 2.5% reflection shows as a highlight, and auto-exposure,
  set for the face, often clips it to a white blob with no inner detail. A paper note lit by the room is several times
  dimmer than the screen; its reflection is a small change laid over the iris's own pattern, which varies by person
  and by eye colour.
- **The eye moves.** Saccades, blinks and head movement change the cornea's angle and position from frame to frame, so
  averaging frames to gain detail (multi-frame super-resolution) needs sub-pixel registration of a target that is a
  few pixels across.
- **Everything else in the room reflects too.** A lamp, a window, a phone lying face up, a second monitor: each makes
  its own highlight, and nothing in a few pixels tells them from a note.

The only case with any signal is a large, bright object held close to the face, such as a phone screen or a sheet
held up. That object also sits in front of the webcam, and the student has to look at it to use it, which is what the
existing eye-contact and reading-like-movement notes already measure.

---

## 3. What the published work shows

**Long et al., "Private Eye: On the Limits of Textual Screen Peeking via Eyeglass Reflections in Video Conferencing"**
(IEEE S&P 2023; arXiv 2205.03971). Abstract, plus the first author's comments to The Register. Reconstructs on-screen
text from reflections in **eyeglasses** during video calls. Reports that text as small as about 10 mm tall on screen
can be recognised with a 720p webcam, which the author put at roughly 50 to 60 pixel fonts on an average laptop
(headings and slide titles), with 4K cameras expected to reach most headings. Accuracy figures come from controlled
lab settings. Bears on Gadfly: this is the best case for the idea, and it reads the student's own **screen**, through
**glasses**, at **headline size**. A spectacle lens is far flatter than the cornea, so it forms a much larger image of
the screen; the cornea alone would do considerably worse (an inference from the geometry, not a result the paper
reports). It does not read paper notes, and it cannot help with students who do not wear glasses. Confidence: high
that webcam-resolution reading of normal-size text is out of reach.
https://arxiv.org/abs/2205.03971

**Backes, Chen, Dürmuth, Lensch and Welk, "Tempest in a Teapot: Compromising Reflections Revisited"** (IEEE S&P 2009).
Abstract and indexed excerpts. Reading a screen from its reflection in a person's **eye** needed a telescope: 36 pt
text from 10 m, improving on an earlier 150 pt at 4 m, with distance scaling with the telescope's aperture. The
earlier paper (Backes, Dürmuth and Unruh, "Compromising Reflections", IEEE S&P 2008) notes eyes are hard because they
move and need long exposures. Bears on Gadfly: even dedicated optics aimed at one eye needed large type; a webcam's
lens is a few millimetres across. Confidence: high.
https://www.mia.uni-saarland.de/Publications/backes-sp09.pdf

**Jenkins and Kerr, "Identifiable Images of Bystanders Extracted from Corneal Reflections"** (PLoS ONE 8(12):
e83325, 2013). Abstract. Recovered faces of bystanders from the eyes in high-resolution, in-focus, frontal, well-lit
portrait photographs (secondary coverage reports a 39-megapixel camera); viewers matched them to photos at 71%
(unfamiliar) and 84% (familiar). Bears on Gadfly: corneal imaging works with a studio camera and a face filling the
frame, roughly 50 to 100 times the pixels on the eye that a webcam gives. Confidence: high.
https://doi.org/10.1371/journal.pone.0083325

**Nishino and Nayar, "Corneal Imaging System: Environment from Eyes"** (IJCV 70: 23-40, 2006). Record. Models the
cornea and a camera together as an uncalibrated catadioptric (mirror plus lens) system to recover what the eye sees.
Bears on Gadfly: the method exists; its demonstrations used high-resolution close-up images. Confidence: medium (only
the record was read).
https://doi.org/10.1007/s11263-006-6274-9

**Ogletree v. Cleveland State University** (N.D. Ohio 2022), as read in
[attention-and-gaze.md](attention-and-gaze.md) Section 3.6 (Full there): a pre-exam room scan at a public university
was held unreasonable, weighing whether it was "truly, and uniquely, effective". **Conijn et al. (2022)**, same note
(Abstract): proctoring had no effect on the temptation to cheat, exam difficulty or performance, but raised test
anxiety. The **CNIL** (délibération 2023-058, same note) names oral defences as formats that need no remote
surveillance.

---

## 4. The room sweep, considered on its own

What a sweep could do in Gadfly, and what stops it:

| Form | What it catches | Why it falls short |
|---|---|---|
| Recorded sweep a reviewer watches later | Notes, a second person, a phone, visible at that moment | Gadfly stores no frames or video (`docs/PRIVACY.md`); a recording is the most sensitive data the product would hold, from inside students' homes. Notes go up after the sweep ends. |
| On-device object detection (phone, book, paper, person) sending a count | A phone or a person in view during the sweep | Detector accuracy on paper notes and on cluttered home rooms is unmeasured; it would be one more low-confidence note, and it is defeated by timing. A second person is already noted by the face checks. |
| Unrecorded "show us" step, nothing analysed | Nothing directly; it states the expectation | Cheap and private. Its effect is deterrence only, and the evidence that proctoring deters is weak (Conijn et al.). Adds a step and some anxiety before a spoken exam. |

The earpiece case (GAD-T449) is the same: an ear shown at the start says nothing about an earpiece put in a minute
later, and a webcam at 640 width cannot see an in-ear device anyway.

---

## 5. What would change this answer

- **Camera resolution and capture size.** If Gadfly moved to 4K capture (a large bandwidth and battery cost, and most
  laptop cameras are 720p or 1080p), a held-up sheet or phone would reach about 10 pixels in the cornea. That is still
  presence, not content, and still swamped by other highlights; it would not justify a note.
- **Supervised rooms.** In a supervised testing room a human invigilator does what a sweep pretends to do. That is
  the integrity guide's advice already for high-stakes use.
- **Pilot data.** If the pilot (`docs/PILOT.md`) finds that students do use notes and that the follow-up questions do
  not expose it, the next lever is the question design, not the camera.

---

## 6. Follow-up

- Filed as GAD-T653 (split of GAD-T244): an owner decision on whether to offer the optional, unrecorded, Strict-only start
  sweep (desk, monitor edges, ears) described in Section 1, and to build it if approved. The corneal reflection part
  is closed by this note: not feasible, not fair, not to be built.
