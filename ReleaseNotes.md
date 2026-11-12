# Smarter Diagnosis and Treatment: release notes

Newest version first.

## 1.0.1

### Compatibility
- Works with Loupito75's Hospital Flow Optimizer, Hospital Runtime Optimizer, Hospital Traffic Control, Hospital Always Lit, Hospital Radio and Hospital Mod Update Checker.
- Nurse bedside exams no longer override Hospital Flow Optimizer's Trauma Center protection while it waits for busy equipment for an ER observation patient.
- Kept undressed: a patient is no longer dressed for a walk that Hospital Traffic Control holds back.

## 1.0.0

First release.

### Diagnosis
- **New diagnosis percentages.** Only symptoms that were actually checked count. In vanilla, a symptom nobody has tested for yet counts against every diagnosis that lists it, so early percentages were often misleading. Now:
  - a symptom the patient shows raises every diagnosis that lists it, more so when it is common for that diagnosis;
  - a symptom that was tested and not found lowers the diagnoses that list it;
  - a missing main symptom, or a missing symptom that a diagnosis always has, rules that diagnosis out;
  - symptoms nobody has tested for yet do not count.
- Percentages are shown as whole numbers, with `<1%` and `>99%` at the extremes.
- **Wrong diagnoses are fixed for AI patients.** When a later exam shows that the diagnosis is wrong, it is dropped together with the planned treatments that only it needed (and a planned hospitalization if no symptom needs it), and the doctor goes on examining. The player still gets the vanilla notification.
- NPC doctors diagnose before reaching the department's certainty level only with real evidence: at least two of the diagnosis's symptoms seen (or its main symptom), at least 66% likelihood, and a clear lead over the runner-up.

### Examinations
- The available examinations list only shows exams that can still reveal a hidden symptom of one of the possible diagnoses, plus the interview, Differential Diagnosis and surgery complication checks. Exams that cannot change anything are hidden.
- The list is sorted by usefulness: how much the exam's possible results would tell the likely diagnoses apart (expected information gain).
- NPC doctors choose exams by usefulness instead of always taking the highest-priority one. Better diagnosticians pick the most useful exam more often. Doctors slightly prefer exams they can do in their own office, and exam priority still counts.
- Differential Diagnosis gets more attractive as the list of possible diagnoses shrinks. Doctors with the advanced diagnosis specialization pick it more often.
- In the Emergency department, NPC doctors skip low-priority exams while a more important useful exam is available, so patients are not sent to observation too early.
- Lab sampling and lab testing exams are treated as the same exam for highlights and tooltips.
- Exam priorities changed: Evaluation 22 → 45 (done early, since it is cheap and reveals a lot), Differential Diagnosis 15 → 31 (so choosing it no longer sends ER patients to observation).

### Wards
- **Nurse bedside exams.** Ward nurses do the simple exams of hospitalized patients at the bedside (blood pressure, temperature, pulse oximetry, heart monitoring, blood, urine and stool samples), so doctors are free for the rest. If all nurses are busy the exam waits for one; the exam list and the waiting can be changed in the config.
- **Basic exams in the ward.** Exams with no or basic equipment take place in the ward instead of the diagnostic unit. Regular wards and HDUs therefore require an equipment cabinet, a CRP scanner and a hand sanitizer. The built-in ward prefabs (also those of Infectious Diseases and Traumatology) are replaced with versions that contain them; wards built before installing the mod show missing equipment until these items are added.
- **Bundled sampling.** Several queued blood samples are taken in one blood draw, and urine and stool samples in one toilet trip or bedside visit. Each exam still sends its own sample to the lab.
- **Kept undressed.** A clinic patient undressed for an exam stays undressed while their own doctor goes on examining them, and dresses again when they walk for an exam that needs no undressing, a treatment starts or they leave the doctor. Left to Hospital Fixes while its *Avoid redundant examination clothing changes* setting is on.

### Patient card: highlights and tooltips
- Hover a possible diagnosis to highlight the examinations that can reveal one of its hidden symptoms.
- Hover an examination to highlight the possible diagnoses it can help decide.
- Hover a finished examination to highlight the symptoms it revealed, or hover a symptom to highlight the exams that revealed it.
- Hover a treatment to highlight the symptoms and diagnoses it treats, or hover a symptom or diagnosis to highlight its treatments.
- Examination tooltips list the relevant possible diagnoses.
- Diagnosis tooltips list the examinations that can reveal its symptoms, colored by whether they were done and what they found.
- Symptom tooltips list the examinations that can reveal the symptom and mark the ones already done.

### Treatment
- **Collapse-risk symptoms are treated first.** Applies to symptoms that can make a patient collapse.
  - Clinic patients get the treatment before any further exam or transfer. If it cannot start yet, they wait in the waiting room (up to about an hour) instead of being sent away untreated.
  - Hospitalized patients get it before a queued surgery. The surgery is held back for up to 2 hours.
  - During surgery, the OR team handles a pending collapse on the table and gives the treatment right away.
- **Treatment threshold per department.** A new Treatment section in each department's management window sets how far AI doctors go beyond the treatments listed for the diagnosis: Diagnosis only (vanilla), High, Medium or Low hazard. Extra treatments never add surgery or a higher care level. The setting is saved with your hospital.
- **Treatment bundling.** A doctor gives all of a patient's pending advice and medication in one visit, instead of one visit and animation per item.
- **Receipts across the desk.** When the patient stands right across the doctor's desk, the doctor hands the receipt over the desk instead of walking around it.
- **Fast track.** A clinic patient whose exam reveals a collapse-risk symptom moves up the queue (by vanilla priority, then arrival), and skips non-urgent needs while waiting, unless enough patients are ahead or a need is pressing.
- In the ER, a patient diagnosed with a condition that belongs to another existing department is transferred there instead of being forced into ER observation first.

### ICU step-down
- ICU patients who no longer need intensive care can move to a department's HDU (or ward). The patient must be diagnosed, have no high-hazard symptom, no pending collapse, no ICU-only treatment and no unexamined dangerous symptom, and must have stayed at least 8 hours.
- Manual: use the department button on the patient card. Its tooltip says why the patient cannot move yet, or that they are ready.
- Automatic: AI patients are moved after a nurse check-up finds them stable.

### Vanilla fixes
- A patient called by a walking doctor follows at once, instead of waiting until the doctor has walked back.
- Patients no longer stand on the printer or paper-holder tile in rotated doctor offices.
- A clinic patient on the way to an exam by a lab specialist goes to that exam, instead of sometimes getting a treatment queued first.

### Configuration
- `ModSmarterDiagnosisAndTreatment.Config.xml` in the mod folder can switch each feature on or off and tune its numbers. Every setting has a comment with its range and default. Restart the game after editing. Delete the file to restore the defaults.

### Other
- Available in 20 languages.
- Works with all DLCs, any combination of them, or none.
- Works with Loupito75's Hospital Porters, Hospital Care Level Transfer, Hospital Fixes, Hospital EMS, Hospital Patient Life and Hospital Shift Handover mods.
- Can be added to an existing save.
