# Smarter Diagnosis for Project Hospital
*Created with the help of AI*

This mod makes the diagnosis and treatment aspects of the game more intelligent for both the AI and the player.

## Smarter Diagnosis

- Improved how the probability of potential diagnoses is calculated depending on symptoms.
- Examinations that prove that a symptom does not exist now affect the probability of possible diagnoses depending on how likely or rare that symptom is for that diagnosis.
- Exams that prove the absence of the main symptom of a disease now eliminate it from the possible diagnoses.
- Improved how AI doctors choose from the possible diagnoses based on the new logic.

## Smarter Examinations

- The available examinations list now shows only those exams that can still reveal something about the possible diagnoses.
- The list is sorted by relevance, so the next best exam (most relevant for most of top possible diagnoses for that patient) is at the top.
- Instead of the vanilla fixed order of exams by priority, AI doctors now choose exams by how useful and relevant they are depending on the patient's possible diagnoses. Doctors with higher Diagnostics skill choose better exams more often, and doctors prefer exams they can do in their own office before sending to labs or radiology.
- Differential Diagnosis gains more priority as the list of possible diagnoses narrows down.
- In the ER, doctors no longer send patients to observation too early just because they picked a minor exam.

## Smarter Tooltips and Highlighting

Hover over things on the patient card and related items light up. Tooltips also tell you more: exams list their relevant diagnoses, diagnoses list their relevant exams, and symptoms show which exams were already done.

- Hovering over a possible diagnosis now lists and highlights the exams that can confirm or rule it out
- Hovering over an exam lists and highlights the diagnoses it can help decide
- Hovering over a finished exam highlights the symptoms it found (and the other way round)
- Hovering over a treatment highlights the symptoms and diagnoses it treats (and the other way round)

## Smarter Treatments

- **Collapse risks come first.** A patient with a symptom that can make them collapse gets that treatment before more exams, transfers or a queued surgery. Clinic patients wait for it instead of being sent home untreated.
- **Safer surgery.** The OR team handles a collapse *during surgery* on the table and gives urgent treatments right away!
- **Choose how thorough your doctors are.** A new Treatment section in each department's management window lets you decide whether AI doctors also treat other symptoms beyond the diagnosis-only treatments: Diagnosis only (vanilla), High, Medium or Low hazard.
- **Efficient animations.** Doctors now combine the prescriptions of multiple similar-treatments into a single animation (lengthened by the number of treatments) instead of each prescription doing a full animation from start to finish. The treatment being given is still shown in a speech bubble on the doctor.
- ER patients whose diagnosis belongs to another department are transferred there instead of being held in ER observation.

## Smarter ICU

In vanilla, an ICU patient stays for the full stay even when they are long since stable. In this mod it works more like real life:

- A patient can be moved from ICU to another department's HDU if all of the following conditions are fulfilled: the patient is diagnosed, is stable, is free of any potential collapse risks and has been in the ICU for at least 8 hours. The tooltip tells you exactly why they can't move yet.
- AI patients are moved automatically after a nurse check-up finds that they fulfil the conditions.

## Smart Configurations

Everything can be switched off or tuned in **ModSmarterDiagnosisAndTreatment.Config.xml** in the mod folder. Every setting is explained in the file. Restart the game after editing.

*Note: a mod update may replace this file, so keep a copy of your changes.*

## Smart Compatibility

- Can be added to an existing save.
- Available in 20 languages.
- Works with Loupito75's Hospital Porters and Hospital Care Level Transfer.
- Changes the Priority of the Evaluation and Differential Diagnosis exams. Other mods that change these two exams may conflict.

Full changelog: see ReleaseNotes.md in the mod folder.
