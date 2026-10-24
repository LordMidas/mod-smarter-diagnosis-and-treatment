# Smarter Diagnosis and Treatment: release notes

Newest version first.

## 1.0.0

First release.

### Diagnosis
- **New diagnosis percentages.** Only symptoms that were actually checked count. In vanilla, a symptom nobody has tested for yet counts against every diagnosis that lists it, so early percentages were often misleading. Now:
  - a symptom the patient shows raises every diagnosis that lists it, more so when it is common for that diagnosis;
  - a symptom that was tested and not found lowers the diagnoses that list it;
  - a missing main symptom, or a missing symptom that a diagnosis always has, rules that diagnosis out;
  - symptoms nobody has tested for yet do not count.
- Percentages are shown as whole numbers, with `<1%` and `>99%` at the extremes.
- NPC doctors diagnose before reaching the department's certainty level only with real evidence: at least two of the diagnosis's symptoms seen (or its main symptom), at least 66% likelihood, and a clear lead over the runner-up.

### Examinations
- The available examinations list only shows exams that can still reveal a hidden symptom of one of the possible diagnoses, plus the interview, Differential Diagnosis and surgery complication checks. Exams that cannot change anything are hidden.
- The list is sorted by usefulness: exams that can help decide between the most likely diagnoses come first.
- NPC doctors choose exams by usefulness instead of always taking the highest-priority one. Better diagnosticians pick the most useful exam more often. Doctors slightly prefer exams they can do in their own office, and exam priority still counts.
- Differential Diagnosis gets more attractive as the list of possible diagnoses shrinks. Doctors with the advanced diagnosis specialization pick it more often.
- In the Emergency department, NPC doctors skip low-priority exams while a more important useful exam is available, so patients are not sent to observation too early.
- Lab sampling and lab testing exams are treated as the same exam for highlights and tooltips.
- Exam priorities changed: Evaluation 22 → 45 (done early, since it is cheap and reveals a lot), Differential Diagnosis 15 → 31 (so choosing it no longer sends ER patients to observation).

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
- In the ER, a patient diagnosed with a condition that belongs to another existing department is transferred there instead of being forced into ER observation first.

### ICU step-down
- ICU patients who no longer need intensive care can move to a department's HDU (or ward). The patient must be diagnosed, have no high-hazard symptom, no pending collapse, no ICU-only treatment and no unexamined dangerous symptom, and must have stayed at least 8 hours.
- Manual: use the department button on the patient card. Its tooltip says why the patient cannot move yet, or that they are ready.
- Automatic: AI patients are moved after a nurse check-up finds them stable.

### Configuration
- `ModSmarterDiagnosisAndTreatment.Config.xml` in the mod folder can switch each feature on or off and tune its numbers. Every setting has a comment with its range and default. Restart the game after editing. Delete the file to restore the defaults.

### Other
- Available in 20 languages.
- Works with Loupito75's Hospital Porters and Hospital Care Level Transfer mods.
- Can be added to an existing save.
