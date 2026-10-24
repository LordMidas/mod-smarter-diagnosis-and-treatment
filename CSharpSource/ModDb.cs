using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>Lazily cached database entries the mod looks up repeatedly.</summary>
    internal static class ModDb
    {
        private static GameDBExamination interview;
        private static GameDBExamination differentialDiagnosis;
        private static GameDBExamination autopsy;
        private static GameDBTreatment observationTreatment;
        private static GameDBTreatment traumaTreatment;
        private static GameDBDepartment emergencyDepartment;
        private static GameDBProcedure collapseMediumProcedure;
        private static GameDBProcedure collapseHighProcedure;
        private static GameDBTreatment icuTreatment;
        private static GameDBTreatment highPriorityTreatment;
        private static GameDBTreatment normalTreatment;
        private static GameDBDepartment icuDepartment;
        private static GameDBDepartment radiologyDepartment;
        private static GameDBDepartment labDepartment;
        private static GameDBDepartment pathologyDepartment;
        private static GameDBDepartment administrativeDepartment;
        private static GameDBRoomType highPriorityWardRoomType;
        private static GameDBRoomType inpatientWardRoomType;

        public static GameDBExamination Interview
        {
            get { return interview ?? (interview = Database.Instance.GetEntry<GameDBExamination>("EXM_INTERVIEW")); }
        }

        public static GameDBExamination DifferentialDiagnosis
        {
            get { return differentialDiagnosis ?? (differentialDiagnosis = Database.Instance.GetEntry<GameDBExamination>("EXM_DIFFERENTIAL_DIAGNOSIS")); }
        }

        public static GameDBExamination Autopsy
        {
            get { return autopsy ?? (autopsy = Database.Instance.GetEntry<GameDBExamination>("EXM_AUTOPSY")); }
        }

        public static GameDBTreatment ObservationTreatment
        {
            get { return observationTreatment ?? (observationTreatment = Database.Instance.GetEntry<GameDBTreatment>("TRT_HOSPITALIZATION_OBSERVATION")); }
        }

        public static GameDBTreatment TraumaTreatment
        {
            get { return traumaTreatment ?? (traumaTreatment = Database.Instance.GetEntry<GameDBTreatment>("TRT_HOSPITALIZATION_TRAUMA")); }
        }

        public static GameDBDepartment EmergencyDepartment
        {
            get { return emergencyDepartment ?? (emergencyDepartment = Database.Instance.GetEntry<GameDBDepartment>("DPT_EMERGENCY")); }
        }

        public static GameDBProcedure CollapseMediumProcedure
        {
            get { return collapseMediumProcedure ?? (collapseMediumProcedure = Database.Instance.GetEntry<GameDBProcedure>("CONTROL_PROCEDURE_COLLAPSE_MEDIUM")); }
        }

        public static GameDBProcedure CollapseHighProcedure
        {
            get { return collapseHighProcedure ?? (collapseHighProcedure = Database.Instance.GetEntry<GameDBProcedure>("CONTROL_PROCEDURE_COLLAPSE_HIGH")); }
        }

        public static GameDBTreatment IcuTreatment
        {
            get { return icuTreatment ?? (icuTreatment = Database.Instance.GetEntry<GameDBTreatment>("TRT_HOSPITALIZATION_ICU")); }
        }

        public static GameDBTreatment HighPriorityTreatment
        {
            get { return highPriorityTreatment ?? (highPriorityTreatment = Database.Instance.GetEntry<GameDBTreatment>("TRT_HOSPITALIZATION_HIGH_PRIORITY")); }
        }

        public static GameDBTreatment NormalTreatment
        {
            get { return normalTreatment ?? (normalTreatment = Database.Instance.GetEntry<GameDBTreatment>("TRT_HOSPITALIZATION_NORMAL")); }
        }

        public static GameDBDepartment IcuDepartment
        {
            get { return icuDepartment ?? (icuDepartment = Database.Instance.GetEntry<GameDBDepartment>("DPT_ICU")); }
        }

        public static GameDBDepartment RadiologyDepartment
        {
            get { return radiologyDepartment ?? (radiologyDepartment = Database.Instance.GetEntry<GameDBDepartment>("DPT_RADIOLOGY")); }
        }

        public static GameDBDepartment LabDepartment
        {
            get { return labDepartment ?? (labDepartment = Database.Instance.GetEntry<GameDBDepartment>("DPT_LAB")); }
        }

        // The pathology and administrative departments belong to a DLC and are null without it.
        public static GameDBDepartment PathologyDepartment
        {
            get { return pathologyDepartment ?? (pathologyDepartment = Database.Instance.GetEntry<GameDBDepartment>("DLC_DPT_PATHOLOGY")); }
        }

        public static GameDBDepartment AdministrativeDepartment
        {
            get { return administrativeDepartment ?? (administrativeDepartment = Database.Instance.GetEntry<GameDBDepartment>("DLC_DPT_ADMINISTRATIVE")); }
        }

        public static GameDBRoomType HighPriorityWardRoomType
        {
            get { return highPriorityWardRoomType ?? (highPriorityWardRoomType = Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_HIGH_PRIORITY_INPATIENT_WARD")); }
        }

        public static GameDBRoomType InpatientWardRoomType
        {
            get { return inpatientWardRoomType ?? (inpatientWardRoomType = Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_INPATIENT_WARD")); }
        }

        // Interview-style procedure scripts only reveal symptoms the patient complains about.
        public static bool RevealsOnlyComplainedSymptoms(GameDBExamination examination)
        {
            if (examination == null || examination.Procedure == null)
                return false;

            string script = examination.Procedure.ProcedureScript;
            return script == "ProcedureScriptExaminationDoctorsInterview" ||
                script == "ProcedureScriptExaminationReception" ||
                script == "ProcedureScriptExaminationReceptionFast";
        }
    }
}
