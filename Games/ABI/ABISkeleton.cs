using System;
using System.Numerics;
using MamboDMA.Services;

namespace MamboDMA.Games.ABI
{
    internal static class Skeleton
    {
        // indices
        private const int Pelvis = 1;
        private const int Spine_01 = 12, Spine_02 = 13, Spine_03 = 14, Neck = 15, Head = 16;
        private const int Thigh_L = 2, Calf_L = 4, Foot_L = 5;
        private const int Thigh_R = 7, Calf_R = 9, Foot_R = 10;
        private const int Clavicle_L = 50, UpperArm_L = 51, LowerArm_L = 52, Hand_L = 54;
        private const int Clavicle_R = 20, UpperArm_R = 21, LowerArm_R = 22, Hand_R = 24;

        private static readonly int[] _fetch = new int[]
        {
            Pelvis, Spine_01, Spine_02, Spine_03, Neck, Head,
            Clavicle_L, UpperArm_L, LowerArm_L, Hand_L,
            Clavicle_R, UpperArm_R, LowerArm_R, Hand_R,
            Thigh_L, Calf_L, Foot_L, Thigh_R, Calf_R, Foot_R
        };

        public const int IDX_Pelvis = 0;
        public const int IDX_Spine_01 = 1;
        public const int IDX_Spine_02 = 2;
        public const int IDX_Spine_03 = 3;
        public const int IDX_Neck = 4;
        public const int IDX_Head = 5;
        public const int IDX_Clavicle_L = 6;
        public const int IDX_UpperArm_L = 7;
        public const int IDX_LowerArm_L = 8;
        public const int IDX_Hand_L = 9;
        public const int IDX_Clavicle_R = 10;
        public const int IDX_UpperArm_R = 11;
        public const int IDX_LowerArm_R = 12;
        public const int IDX_Hand_R = 13;
        public const int IDX_Thigh_L = 14;
        public const int IDX_Calf_L = 15;
        public const int IDX_Foot_L = 16;
        public const int IDX_Thigh_R = 17;
        public const int IDX_Calf_R = 18;
        public const int IDX_Foot_R = 19;

        public struct DebugInfo
        {
            public ulong Mesh;
            public string Note;
            public FTransform ComponentToWorld_Used;
            public int SampleCount;
            public int[] SampleIndices;
            public Vector3[] SampleComp;
            public Vector3[] SampleWorld;
        }
        public static DebugInfo LastDebug;

        private static bool IsSane(in FTransform t)
        {
            bool finite =
                float.IsFinite(t.Scale3D.X) && float.IsFinite(t.Scale3D.Y) && float.IsFinite(t.Scale3D.Z) &&
                float.IsFinite(t.Translation.X) && float.IsFinite(t.Translation.Y) && float.IsFinite(t.Translation.Z) &&
                float.IsFinite(t.Rotation.W);
            bool nonZeroScale = Math.Abs(t.Scale3D.X) > 1e-4f || Math.Abs(t.Scale3D.Y) > 1e-4f || Math.Abs(t.Scale3D.Z) > 1e-4f;
            bool plausibleT = Math.Abs(t.Translation.X) < 5e6f && Math.Abs(t.Translation.Y) < 5e6f && Math.Abs(t.Translation.Z) < 5e6f;
            return finite && nonZeroScale && plausibleT;
        }

        public static bool TryGetWorldBones(ulong mesh, ulong root, in FTransform ctwOverride,
                                            out Vector3[] worldPoints, out DebugInfo dbg)
        {
            dbg = default; dbg.Mesh = mesh; worldPoints = null;

            try
            {
                using var map = DmaMemory.Scatter();
                var r = map.AddRound(false);

                ulong arr = mesh + ABIOffsets.USkeletalMeshComponent_CachedComponentSpaceTransforms;
                r[0].AddValueEntry<ulong>(2, arr + 0x0);
                r[0].AddValueEntry<int>(3, arr + 0x8);
                map.Execute();

                var ctw = ctwOverride;
                if (!IsSane(ctw)) { dbg.Note = "CTW override invalid"; return false; }
                dbg.ComponentToWorld_Used = ctw;

                if (!r[0].TryGetValue(2, out ulong data) || data == 0 ||
                    !r[0].TryGetValue(3, out int count) || count <= 0)
                { dbg.Note = "Bones header invalid"; return false; }

                using var map2 = DmaMemory.Scatter();
                var r2 = map2.AddRound(false);
                const int SZ = 0x30;
                for (int i = 0; i < _fetch.Length; i++)
                    r2[i].AddValueEntry<FTransform>(0, data + (ulong)(_fetch[i] * SZ));
                map2.Execute();

                const int SAMPLE = 8;
                dbg.SampleCount = Math.Min(SAMPLE, _fetch.Length);
                dbg.SampleIndices = new int[dbg.SampleCount];
                dbg.SampleComp = new Vector3[dbg.SampleCount];
                dbg.SampleWorld = new Vector3[dbg.SampleCount];

                worldPoints = new Vector3[_fetch.Length];
                for (int i = 0; i < _fetch.Length; i++)
                {
                    if (!r2[i].TryGetValue(0, out FTransform boneCS))
                    { dbg.Note = "Bone read failed"; return false; }

                    var ws = ABIMath.TransformPosition(ctw, boneCS.Translation);
                    worldPoints[i] = ws;

                    if (i < dbg.SampleCount)
                    {
                        dbg.SampleIndices[i] = _fetch[i];
                        dbg.SampleComp[i] = boneCS.Translation;
                        dbg.SampleWorld[i] = ws;
                    }
                }

                dbg.Note = "ok";
                return true;
            }
            catch (Exception ex) { dbg.Note = $"Exception: {ex.Message}"; return false; }
        }

        public static void Draw(ImGuiNET.ImDrawListPtr list, Vector3[] wp, FMinimalViewInfo cam, float w, float h, uint color)
        {
            void seg(int a, int b)
            {
                if (ABIMath.WorldToScreen(wp[a], cam, w, h, out var A) &&
                    ABIMath.WorldToScreen(wp[b], cam, w, h, out var B))
                {
                    list.AddLine(A, B, color, 1.5f);
                }
            }

            // spine
            seg(IDX_Pelvis, IDX_Spine_01);
            seg(IDX_Spine_01, IDX_Spine_02);
            seg(IDX_Spine_02, IDX_Spine_03);
            seg(IDX_Spine_03, IDX_Neck);
            seg(IDX_Neck, IDX_Head);

            // arms
            seg(IDX_Spine_03, IDX_Clavicle_L);
            seg(IDX_Clavicle_L, IDX_UpperArm_L);
            seg(IDX_UpperArm_L, IDX_LowerArm_L);
            seg(IDX_LowerArm_L, IDX_Hand_L);

            seg(IDX_Spine_03, IDX_Clavicle_R);
            seg(IDX_Clavicle_R, IDX_UpperArm_R);
            seg(IDX_UpperArm_R, IDX_LowerArm_R);
            seg(IDX_LowerArm_R, IDX_Hand_R);

            // legs
            seg(IDX_Pelvis, IDX_Thigh_L);
            seg(IDX_Thigh_L, IDX_Calf_L);
            seg(IDX_Calf_L, IDX_Foot_L);

            seg(IDX_Pelvis, IDX_Thigh_R);
            seg(IDX_Thigh_R, IDX_Calf_R);
            seg(IDX_Calf_R, IDX_Foot_R);
        }
    }
}
