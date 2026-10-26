using HarmonyLib;
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>
    /// Vanilla fix (always on). Without a patient seat, the patient stands at the doctor's GetInteractionPosition: the
    /// first free neighbour of the work chair in the fixed world order NW, NE, SE, SW. A paper holder or printer is an
    /// attachment on the desk, so its use tile counts as free, and in two of the four rotations of an office (chair
    /// facing NW or SW) the patient is sent onto the tile where the doctor stands to write the receipt. Here that tile
    /// is skipped in favour of the next free neighbour of the chair, with vanilla's other conditions unchanged.
    /// </summary>
    [HarmonyPatch(typeof(Behavior), nameof(Behavior.GetInteractionPosition))]
    public static class Behavior_GetInteractionPosition
    {
        public static void Postfix(Behavior __instance, ref Vector2i __result)
        {
            EmployeeComponent employee = __instance.m_entity.GetComponent<EmployeeComponent>();
            if (employee == null)
                return;

            TileObject chair = employee.GetWorkChair();
            if (chair == null)
                return;

            Vector2i chairPosition = chair.m_state.m_position;
            int floorIndex = chair.GetFloorIndex();
            Vector2i offset = __result - chairPosition;
            if (System.Math.Abs(offset.m_x) + System.Math.Abs(offset.m_y) != 1 || !IsReceiptUseTile(__result, floorIndex))
                return;

            Vector2i[] directions = Vector2i.ALL_DIRECTIONS;
            for (int index = 0; index < directions.Length; index++)
            {
                Vector2i tile = chairPosition + directions[index];
                if (MapScriptInterface.Instance.GetCenterObjectAt(tile, floorIndex) == null &&
                    MapScriptInterface.Instance.IsAccessibleNoWalls(tile, chairPosition, floorIndex) &&
                    !__instance.IsBehind(tile, chair) &&
                    !IsReceiptUseTile(tile, floorIndex))
                {
                    __result = tile;
                    return;
                }
            }
        }

        /// <summary>The tile is where staff stand to use an adjacent paper holder or printer.</summary>
        public static bool IsReceiptUseTile(Vector2i tile, int floorIndex)
        {
            Vector2i[] directions = Vector2i.ALL_DIRECTIONS;
            for (int index = 0; index < directions.Length; index++)
            {
                TileObjects objects = GetTileObjects(tile + directions[index], floorIndex);
                if (objects == null)
                    continue;

                if (IsReceiptObjectUsedFrom(objects.m_centerObject, tile) || IsReceiptObjectUsedFrom(objects.m_attachmentObject, tile))
                    return true;
            }

            return false;
        }

        private static bool IsReceiptObjectUsedFrom(TileObject tileObject, Vector2i tile)
        {
            return tileObject != null && tileObject.HasTag("receipt") && tileObject.GetDefaultUseTile() == tile;
        }

        private static TileObjects GetTileObjects(Vector2i position, int floorIndex)
        {
            Floor floor = Hospital.Instance.m_floors[floorIndex];
            if (position.m_x < 0 || position.m_y < 0 || position.m_x >= floor.m_size.m_x || position.m_y >= floor.m_size.m_y)
                return null;

            return floor.m_tileObjects[position.m_x, position.m_y];
        }
    }
}
