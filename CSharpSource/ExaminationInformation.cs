using System;
using System.Collections.Generic;
using GLib;
using Lopital;
using UnityEngine;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>
    /// How much an examination is expected to narrow down the possible diagnoses: the expected information gain
    /// (entropy now minus expected entropy after the result) as a share 0-100 of the current entropy. Outcomes are
    /// weighed with the symptom rules' raw probabilities (what the game rolls), and each outcome's diagnoses are
    /// rescored with the scoring's own evidence (SymptomEvidence), so a symptom about as likely under every candidate
    /// counts for little and a down-weighted negative result counts as weakly as the scoring treats it.
    /// Used by the NPC exam choice and the exam list sort; the list filter stays with ExaminationRelevance.
    /// </summary>
    public static class ExaminationInformation
    {
        // Above this many revealable symptoms (early interview or physical exam) only the most informative ones are
        // enumerated, so an exam's outcomes stay at 2^8.
        private const int MaxEnumeratedSymptoms = 8;
        private const double MinEntropyBits = 1e-3;
        private const int MaxCachedConditions = 16;

        /// <summary>A patient's cached scoring state, from Prepare; only this class reads its fields.</summary>
        public sealed class CacheEntry
        {
            internal long Signature;
            internal HashSet<GameDBExamination> FinishedExams;
            internal GameDBMedicalCondition[] Candidates;
            internal double[] Prior;
            internal double Entropy;
            internal readonly Dictionary<GameDBExamination, float> Scores = new Dictionary<GameDBExamination, float>();
        }

        // The exam list is re-sorted every other frame while a patient card is open, so scores are kept until the
        // patient's evidence or possible diagnoses change.
        private static readonly Dictionary<MedicalCondition, CacheEntry> cache = new Dictionary<MedicalCondition, CacheEntry>();

        /// <summary>
        /// The patient's scoring state for a loop over many exams: pass it to the 4-argument GetUsefulness so the
        /// cache signature is checked once. Null when the score falls back to relevance for every exam (InformationGain
        /// off, or the diagnosis already settled: one candidate, nothing to narrow). Do not keep it past the loop.
        /// </summary>
        public static CacheEntry Prepare(ProcedureComponent component, MedicalCondition medicalCondition)
        {
            if (!ModConfig.NpcExams.InformationGain || component == null || medicalCondition == null ||
                medicalCondition.m_possibleDiagnoses == null || medicalCondition.m_possibleDiagnoses.Count < 2)
            {
                return null;
            }

            CacheEntry entry = GetEntry(component, medicalCondition);
            return entry.Entropy < MinEntropyBits ? null : entry;
        }

        /// <summary>
        /// Score for the NPC choice and the list sort. Falls back to the relevance score for Differential Diagnosis,
        /// with InformationGain off, and when the diagnosis is already settled (one candidate, nothing to narrow).
        /// </summary>
        public static float GetUsefulness(ProcedureComponent component, MedicalCondition medicalCondition, GameDBExamination examination)
        {
            return GetUsefulness(Prepare(component, medicalCondition), medicalCondition, examination);
        }

        /// <summary>GetUsefulness with the state from Prepare for the same patient.</summary>
        public static float GetUsefulness(CacheEntry entry, MedicalCondition medicalCondition, GameDBExamination examination)
        {
            if (entry == null || examination == null || examination == ModDb.DifferentialDiagnosis)
                return ExaminationRelevance.GetRelevanceScore(examination, medicalCondition);

            float score;
            if (!entry.Scores.TryGetValue(examination, out score))
            {
                score = ComputeScore(entry, medicalCondition, examination);
                entry.Scores[examination] = score;
            }

            return score;
        }

        private static CacheEntry GetEntry(ProcedureComponent component, MedicalCondition medicalCondition)
        {
            List<ExaminationState> finished = component.m_state.m_procedureQueue.m_finishedExaminations;
            List<PossibleDiagnosis> possibleDiagnoses = medicalCondition.m_possibleDiagnoses;

            long signature = finished.Count;
            foreach (Symptom symptom in medicalCondition.m_symptoms)
            {
                if (symptom.m_spawned && !symptom.m_hidden)
                    signature++;
            }

            foreach (PossibleDiagnosis possibleDiagnosis in possibleDiagnoses)
            {
                signature = signature * 31 + possibleDiagnosis.m_diagnosis.Entry.GetHashCode();
                signature = signature * 31 + (long)(possibleDiagnosis.m_probabilityWeighted * 1000f);
            }

            CacheEntry entry;
            if (cache.TryGetValue(medicalCondition, out entry) && entry.Signature == signature)
                return entry;

            if (cache.Count >= MaxCachedConditions)
                cache.Clear();

            entry = new CacheEntry { Signature = signature, FinishedExams = new HashSet<GameDBExamination>() };
            foreach (ExaminationState state in finished)
            {
                if (state.m_examination.Entry != null)
                    entry.FinishedExams.Add(state.m_examination.Entry);
            }

            int count = possibleDiagnoses.Count;
            entry.Candidates = new GameDBMedicalCondition[count];
            entry.Prior = new double[count];
            for (int index = 0; index < count; index++)
            {
                entry.Candidates[index] = possibleDiagnoses[index].m_diagnosis.Entry;
                entry.Prior[index] = possibleDiagnoses[index].m_probabilityWeighted;
            }

            entry.Entropy = Entropy(entry.Prior);
            cache[medicalCondition] = entry;
            return entry;
        }

        private static float ComputeScore(CacheEntry entry, MedicalCondition medicalCondition, GameDBExamination examination)
        {
            bool interview = ModDb.RevealsOnlyComplainedSymptoms(examination);
            List<GameDBSymptom> symptoms = new List<GameDBSymptom>();
            foreach (GameDBMedicalCondition candidate in entry.Candidates)
            {
                if (candidate.Symptoms == null)
                    continue;

                foreach (GameDBSymptomRules rule in candidate.Symptoms)
                {
                    GameDBSymptom symptom = rule?.GameDBSymptomRef?.Entry;
                    if (symptom == null || symptoms.Contains(symptom) ||
                        (interview && !symptom.PatientComplains) ||
                        medicalCondition.HasVisibleSymptom(symptom) ||
                        MedicalCondition_GetPossibleDiagnoses.HasTestedForSymptom(entry.FinishedExams, symptom) ||
                        !ExaminationEquivalence.SymptomListsExamination(symptom, examination))
                    {
                        continue;
                    }

                    symptoms.Add(symptom);
                }
            }

            if (symptoms.Count == 0)
                return 0f;

            GameDBSymptomRules[,] rules = new GameDBSymptomRules[entry.Candidates.Length, symptoms.Count];
            for (int candidate = 0; candidate < entry.Candidates.Length; candidate++)
            {
                GameDBSymptomRules[] candidateRules = entry.Candidates[candidate].Symptoms;
                if (candidateRules == null)
                    continue;

                foreach (GameDBSymptomRules rule in candidateRules)
                {
                    int column = symptoms.IndexOf(rule?.GameDBSymptomRef?.Entry);
                    if (column >= 0)
                        rules[candidate, column] = rule;
                }
            }

            // An interview proves absence only for a symptom the patient would surely complain about.
            bool[] absenceCounts = new bool[symptoms.Count];
            for (int column = 0; column < symptoms.Count; column++)
                absenceCounts[column] = !interview || MedicalCondition_GetPossibleDiagnoses.CanInterviewReveal(symptoms[column]);

            int[] columns = SelectColumns(entry, rules, symptoms, absenceCounts);
            double gain = ExpectedGain(entry, rules, symptoms, absenceCounts, columns);
            return (float)(100d * Math.Max(0d, Math.Min(1d, gain / entry.Entropy)));
        }

        private static int[] SelectColumns(CacheEntry entry, GameDBSymptomRules[,] rules, List<GameDBSymptom> symptoms, bool[] absenceCounts)
        {
            int count = symptoms.Count;
            int[] all = new int[count];
            for (int column = 0; column < count; column++)
                all[column] = column;

            if (count <= MaxEnumeratedSymptoms)
                return all;

            double[] single = new double[count];
            for (int column = 0; column < count; column++)
                single[column] = ExpectedGain(entry, rules, symptoms, absenceCounts, new[] { column });

            Array.Sort(all, (left, right) =>
            {
                int comparison = single[right].CompareTo(single[left]);
                return comparison != 0 ? comparison : left.CompareTo(right);
            });

            int[] selected = new int[MaxEnumeratedSymptoms];
            Array.Copy(all, selected, MaxEnumeratedSymptoms);
            return selected;
        }

        // Entropy of the candidates now minus the expected entropy over every found/absent combination of the
        // selected symptoms (symptoms are rolled independently per diagnosis).
        private static double ExpectedGain(CacheEntry entry, GameDBSymptomRules[,] rules, List<GameDBSymptom> symptoms, bool[] absenceCounts, int[] columns)
        {
            int candidates = entry.Candidates.Length;
            double[] posterior = new double[candidates];
            double expectedEntropy = 0d;
            double totalOutcomeProbability = 0d;

            for (int mask = 0; mask < (1 << columns.Length); mask++)
            {
                double outcomeProbability = 0d;
                bool mainSymptomFound = false;
                for (int bit = 0; bit < columns.Length; bit++)
                {
                    if ((mask & (1 << bit)) != 0 && symptoms[columns[bit]].IsMainSymptom)
                        mainSymptomFound = true;
                }

                for (int candidate = 0; candidate < candidates; candidate++)
                {
                    double likelihood = 1d;
                    double logLikelihood = 0d;
                    bool ruledOut = false;
                    for (int bit = 0; bit < columns.Length; bit++)
                    {
                        int column = columns[bit];
                        bool found = (mask & (1 << bit)) != 0;
                        GameDBSymptomRules rule = rules[candidate, column];
                        double probability = rule == null ? 0d : Mathf.Clamp01(rule.ProbabilityPercent * 0.01f);
                        likelihood *= found ? probability : 1d - probability;

                        if (ruledOut)
                            continue;

                        // A visible symptom the diagnosis does not list rules it out (the scoring's visible-symptom filter).
                        if (rule == null)
                            ruledOut = found;
                        else if (found || absenceCounts[column])
                            ruledOut = !SymptomEvidence.Apply(rule, found, ref logLikelihood);
                    }

                    outcomeProbability += entry.Prior[candidate] * likelihood;
                    posterior[candidate] = ruledOut ? 0d : entry.Prior[candidate] * Math.Exp(logLikelihood);
                }

                if (outcomeProbability <= 0d)
                    continue;

                totalOutcomeProbability += outcomeProbability;
                // A visible main symptom makes the scoring show its diagnosis alone at 100%.
                if (!mainSymptomFound)
                    expectedEntropy += outcomeProbability * Entropy(posterior);
            }

            if (totalOutcomeProbability <= 0d)
                return 0d;

            return entry.Entropy - expectedEntropy / totalOutcomeProbability;
        }

        private static double Entropy(double[] weights)
        {
            double total = 0d;
            foreach (double weight in weights)
                total += weight;

            if (total <= 0d)
                return 0d;

            double entropy = 0d;
            foreach (double weight in weights)
            {
                if (weight <= 0d)
                    continue;

                double share = weight / total;
                entropy -= share * Math.Log(share, 2d);
            }

            return entropy;
        }
    }
}
