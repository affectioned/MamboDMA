using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using MamboDMA;

namespace ArmaReforgerFeeder
{
    /// <summary>
    /// Finds vehicles (≈5 fps) and items (≈2 fps).
    /// Produces immutable snapshots consumed by OverlayUI.SetVehicles/SetItems.
    /// </summary>
    public static class GameObjects
    {
        // ===================== DTOs =====================
        public readonly struct VehicleDto
        {
            public readonly ulong Ptr;
            public readonly string Type;      // prefab path or RTTI fallback
            public readonly string Name;      // pretty / display name (from prefab path/file)
            public readonly string Faction;
            public readonly Vector3f Position;
            public readonly int Distance;
            public VehicleDto(ulong p, string type, string name, string fac, Vector3f pos, int dist)
            { Ptr = p; Type = type; Name = name; Faction = fac; Position = pos; Distance = dist; }
        }
        public static ItemDto[] LatestItemsAll { get; private set; } = Array.Empty<ItemDto>();
        public static float VehicleClusterRadiusM = 8.0f;     // 5–10 m works well
        public static bool  VehicleClusterUseCentroid = true;

        private struct VehMeta { public ulong Ptr; public string Type, Name, Faction; }

        public static int VehFastIntervalMs = 3;      // ≈ players fast loop
        public static int VehSlowIntervalMs = 200;    // light rescan
        public static int VehMaxFrame = 512;          // cap per frame

        private static Thread? _vehFastT, _vehSlowT;

        private static volatile VehMeta[] _vehMeta = Array.Empty<VehMeta>();
        private static volatile bool _vehNeedsRescan = true;

        public readonly struct ItemDto
        {
            public readonly ulong Ptr;
            public readonly string Kind;      // "Weapon" | "Magazine" | "Item"
            public readonly string Type;      // prefab path or RTTI fallback
            public readonly string Name;      // pretty / display name
            public readonly Vector3f Position;
            public readonly int Distance;
            public ItemDto(ulong p, string kind, string type, string name, Vector3f pos, int dist)
            { Ptr = p; Kind = kind; Type = type; Name = name; Position = pos; Distance = dist; }
        }

        public readonly struct VehiclePartDto
        {
            public readonly ulong Ptr;
            public readonly string Type;      // class/path/RTTI
            public readonly string Name;      // pretty name
            public readonly Vector3f Position;
            public readonly int Distance;
            public VehiclePartDto(ulong p, string type, string name, Vector3f pos, int dist)
            { Ptr = p; Type = type; Name = name; Position = pos; Distance = dist; }
        }

        // Keep a separate snapshot (not drawn by overlay)
        public static VehiclePartDto[] LatestVehicleParts { get; private set; } = Array.Empty<VehiclePartDto>();

        // Latest immutable snapshots
        public static VehicleDto[] LatestVehicles { get; private set; } = Array.Empty<VehicleDto>();
        public static ItemDto[]    LatestItems    { get; private set; } = Array.Empty<ItemDto>();

        private const int FactionStringMax = 64;

        // Tunables
        public static float MaxDrawDistance = 600f;    // vehicle/item scan distance
        public static int   MaxScan = 32768;           // cap entity list scan
        public static int   VehiclesHz = 155;          // ~5 fps
        public static int   ItemsHz    = 2;            // ~2 fps
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsPtr(ulong v) => v > 0x10000 && v < 0x0000800000000000UL;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool LooksLikePath(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (s.Length < 4 || s.Length > 256) return false;
            // Enfusion resource-ish
            if (s.Contains(".et", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Contains('/')) return true;
            return false;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string ReadAsciiUtf8(ulong ptr, int max = 256)
        {
            if (!IsPtr(ptr)) return string.Empty;
            var s = DmaMemory.ReadString(ptr, max, Encoding.ASCII) ?? string.Empty;
            if (!LooksLikePath(s))
                s = DmaMemory.ReadString(ptr, max, Encoding.UTF8) ?? string.Empty;
            return s ?? string.Empty;
        }
        // Internal threads
        private static Thread? _itmT;
        private static volatile bool _run;

        public static void Start()
        {
            if (_run) return;
            _run = true;

            // vehicles
            _vehSlowT = new Thread(VehiclesSlowLoop) { IsBackground = true, Name = "GameObjects.Vehicles.Slow", Priority = ThreadPriority.BelowNormal };
            _vehFastT = new Thread(VehiclesFastLoop) { IsBackground = true, Name = "GameObjects.Vehicles.Fast", Priority = ThreadPriority.Highest };
            _vehSlowT.Start();
            _vehFastT.Start();

            // items
            _itmT = new Thread(ItemsLoop) { IsBackground = true, Name = "GameObjects.Items", Priority = ThreadPriority.BelowNormal };
            _itmT.Start();
        }

        public static void Stop()
        {
            _run = false;
            try { _vehFastT?.Join(200); _vehSlowT?.Join(200); _itmT?.Join(200); } catch { }
            _vehFastT = _vehSlowT = _itmT = null;
            _vehMeta = Array.Empty<VehMeta>();
        }

        // ===================== VEHICLES LOOP =====================
        // SLOW: enumerate world, classify vehicles, cache meta (no per-frame math)
        private static void VehiclesSlowLoop()
        {
            while (_run)
            {
                try
                {
                    var meta = RescanVehiclesMeta();
                    Interlocked.Exchange(ref _vehMeta, meta);
                    _vehNeedsRescan = false;
                }
                catch { /* keep thread alive */ }

                Thread.Sleep(VehSlowIntervalMs);
            }
        }

        // FAST: refresh positions for cached vehicles only, publish a fresh frame
        private static void VehiclesFastLoop()
        {
            var sw = new System.Diagnostics.Stopwatch();
            while (_run)
            {
                sw.Restart();
                try
                {
                    var meta = Volatile.Read(ref _vehMeta);
                    if (meta.Length == 0 || _vehNeedsRescan)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    // positions only
                    var pos = new Vector3f[Math.Min(meta.Length, VehMaxFrame)];
                    using (var sc = DmaMemory.Scatter())
                    {
                        var r = sc.AddRound(useCache: false);
                        for (int i = 0; i < pos.Length; i++)
                        {
                            ulong p = meta[i].Ptr; if (p == 0) continue;
                            int idx = i;
                            r[idx].AddValueEntry<Vector3f>(0, p + Off.EntityPosition);
                            r[idx].Completed += (_, cb) => cb.TryGetValue<Vector3f>(0, out pos[idx]);
                        }
                        sc.Execute();
                    }

                    // build per-frame DTOs with proximity clustering (collapse parts into one)
                    Game.UpdateCamera();
                    var cam = Game.Camera.Position;
                    float maxSq = MaxDrawDistance * MaxDrawDistance;
                    float eps   = VehicleClusterRadiusM;
                    float epsSq = eps * eps;

                    // collect candidates first (distance culled)
                    var cand = new List<(int i, Vector3f pos, int dist)>(pos.Length);
                    for (int i = 0; i < pos.Length; i++)
                    {
                        ulong e = meta[i].Ptr; if (e == 0) continue;
                        float dx = pos[i].X - cam.X, dy = pos[i].Y - cam.Y, dz = pos[i].Z - cam.Z;
                        float dsq = dx*dx + dy*dy + dz*dz; if (dsq > maxSq) continue;
                        int dist = (int)MathF.Sqrt(dsq);
                        cand.Add((i, pos[i], dist));
                    }

                    // greedy DBSCAN-ish clustering by spatial radius
                    var used = new bool[cand.Count];
                    var clustered = new List<VehicleDto>(cand.Count);

                    for (int a = 0; a < cand.Count; a++)
                    {
                        if (used[a]) continue;

                        // seed a cluster
                        var idxA = cand[a].i;
                        var members = new List<int> { idxA };
                        used[a] = true;

                        // collect close members
                        for (int b = a + 1; b < cand.Count; b++)
                        {
                            if (used[b]) continue;
                            var idxB = cand[b].i;
                            float dx = pos[idxA].X - pos[idxB].X;
                            float dy = pos[idxA].Y - pos[idxB].Y;
                            float dz = pos[idxA].Z - pos[idxB].Z;
                            if (dx*dx + dy*dy + dz*dz <= epsSq)
                            {
                                members.Add(idxB);
                                used[b] = true;
                            }
                        }

                        // choose representative OR centroid
                        Vector3f anchor;
                        int rep = members[0];

                        if (VehicleClusterUseCentroid)
                        {
                            // centroid anchor
                            double sx = 0, sy = 0, sz = 0;
                            for (int k = 0; k < members.Count; k++)
                            {
                                var p = pos[members[k]];
                                sx += p.X; sy += p.Y; sz += p.Z;
                            }
                            anchor = new Vector3f((float)(sx / members.Count), (float)(sy / members.Count), (float)(sz / members.Count));
                        }
                        else
                        {
                            // pick best-scoring member as representative
                            int bestScore = int.MinValue;
                            for (int k = 0; k < members.Count; k++)
                            {
                                int idx = members[k];
                                int s = ScoreVehicleName(meta[idx].Name, meta[idx].Type);
                                if (s > bestScore) { bestScore = s; rep = idx; }
                            }
                            anchor = pos[rep];
                        }

                        // derive a display name and type for the cluster
                        string name = meta[rep].Name;
                        string type = meta[rep].Type;
                        string fac  = meta[rep].Faction;
                        ulong  ptr  = meta[rep].Ptr;

                        // tiny nudge so marker doesn’t hide inside the hull
                        var nudged = NudgeTowardCamera(anchor, 3.0f);

                        // distance for sorting
                        float ddx = nudged.X - cam.X, ddy = nudged.Y - cam.Y, ddz = nudged.Z - cam.Z;
                        int dist  = (int)MathF.Sqrt(ddx*ddx + ddy*ddy + ddz*ddz);
                                    string[] UH1Fix = { "UH1H Int" };
                        if (HasAny(name, UH1Fix))
                            name = "UH1H";
                        clustered.Add(new VehicleDto(ptr, type, name, fac, nudged, dist));
                    }

                    // final frame (closest first, limit)
                    var frame = clustered.OrderBy(v => v.Distance).Take(VehMaxFrame).ToArray();
                    LatestVehicles = frame;
                    //OverlayUI.SetVehicles(frame, Game.Screen.W, Game.Screen.H);
                }
                catch
                {
                    _vehNeedsRescan = true;
                }

                // pace like Players.FastLoop
                int spent = (int)sw.ElapsedMilliseconds;
                int remain = VehFastIntervalMs - spent;
                if (remain > 1) Thread.Sleep(remain - 1);
            }
        }

        private static VehMeta[] RescanVehiclesMeta()
        {
            if (!TryGetWorld(out ulong gw)) return Array.Empty<VehMeta>();
            if (!DmaMemory.Read(gw + Off.EntityList, out ulong list) || list == 0) return Array.Empty<VehMeta>();
            if (!DmaMemory.Read(gw + Off.EntityCount, out int count) || count <= 0) return Array.Empty<VehMeta>();

            int take = Math.Min(count, MaxScan);
            var ents = ReadEntityArray(list, count, take);
            if (ents.Length == 0) return Array.Empty<VehMeta>();

            // Prefetch prefab + faction (NO positions here)
            var prefab = new ulong[ents.Length];
            var facComp = new ulong[ents.Length];

            using (var sc = DmaMemory.Scatter())
            {
                var r = sc.AddRound(useCache: true);
                for (int i = 0; i < ents.Length; i++)
                {
                    int idx = i;
                    r[idx].AddValueEntry<ulong>(0, ents[i] + Off.PrefabMgrVic);
                    r[idx].AddValueEntry<ulong>(1, ents[i] + Off.FactionComponent);
                    r[idx].Completed += (_, cb) =>
                    {
                        cb.TryGetValue<ulong>(0, out prefab[idx]);
                        cb.TryGetValue<ulong>(1, out facComp[idx]);
                    };
                }
                sc.Execute();
            }

            var vehicles = new List<VehMeta>(256);

            for (int i = 0; i < ents.Length; i++)
            {
                ulong e = ents[i]; if (e == 0) continue;

                string typePath = TryReadPrefabType(prefab[i]);   // "Vehicle/Truck/M923A1.et"
                string nameNice = TryReadPrefabName(prefab[i]);   // "M923A1"
                string faction = TryReadFaction(facComp[i]);
                string rtti = DmaMemory.Rtti.ReadRtti(e) ?? "";

                if (string.IsNullOrWhiteSpace(typePath)) typePath = rtti;
                if (string.IsNullOrWhiteSpace(nameNice)) nameNice = NiceFromPath(typePath) ?? NiceFromType(typePath);

                // Drop parts/lights/supply/etc before we ever touch positions
                if (IsVehiclePart(typePath, nameNice, rtti)) continue;
                if (!LooksLikeVehicle(typePath, nameNice, e)) continue;

                vehicles.Add(new VehMeta
                {
                    Ptr = e,
                    Type = typePath ?? "",
                    Name = nameNice ?? "",
                    Faction = faction ?? ""
                });
            }

            // Stable UI order
            return vehicles.OrderBy(v => v.Name).Take(VehMaxFrame * 2).ToArray();
        }

        // ===================== ITEMS LOOP =====================
        private static void ItemsLoop()
        {
            int sleepMs = Math.Max(10, 1000 / Math.Max(1, ItemsHz));
            var sw = new System.Diagnostics.Stopwatch();

            while (_run)
            {
                sw.Restart();
                try
                {
                    // 1) raw (unfiltered)
                    var all = ScanItems();
                    LatestItemsAll = all;

                    // 2) what the overlay should draw (filtered)
                    LatestItems = all.Where(i => !Players.IsItemCarried(i.Ptr, i.Position, i.Kind, i.Name, out _))
                                     .OrderBy(i => i.Distance)
                                     .Take(1024)
                                     .ToArray();
                }
                catch { /* keep thread alive */ }

                int remain = sleepMs - (int)sw.ElapsedMilliseconds;
                if (remain > 1) Thread.Sleep(remain);
            }
        }

        private static Vector3f NudgeTowardCamera(Vector3f worldPos, float metersForwardToCamera = 12.0f)
        {
            var cam = Game.Camera.Position;
            var dx = cam.X - worldPos.X;
            var dy = cam.Y - worldPos.Y;
            var dz = cam.Z - worldPos.Z;
            var len = MathF.Sqrt(dx*dx + dy*dy + dz*dz);
            if (len < 1e-3f) return worldPos;
            float s = metersForwardToCamera / len;
            return new Vector3f(worldPos.X + dx * s, worldPos.Y + dy * s, worldPos.Z + dz * s);
        }

        // ===================== SCANNERS =====================
        private static VehicleDto[] ScanVehicles()
        {
            if (!TryGetWorld(out ulong gw)) return Array.Empty<VehicleDto>();

            // Entities list
            if (!DmaMemory.Read(gw + Off.EntityList, out ulong list) || list == 0) return Array.Empty<VehicleDto>();
            if (!DmaMemory.Read(gw + Off.EntityCount, out int count) || count <= 0) return Array.Empty<VehicleDto>();

            int take = Math.Min(count, MaxScan);
            var ents = ReadEntityArray(list, count, take);
            if (ents.Length == 0) return Array.Empty<VehicleDto>();

            Game.UpdateCamera();
            var cam = Game.Camera.Position;
            float maxSq = MaxDrawDistance * MaxDrawDistance;

            using var map = DmaMemory.Scatter();
            var rd = map.AddRound(useCache: true);

            var posTmp   = new Vector3f[ents.Length];
            var prefab   = new ulong[ents.Length];
            var facComp  = new ulong[ents.Length];

            for (int i = 0; i < ents.Length; i++)
            {
                int idx = i;
                rd[idx].AddValueEntry<Vector3f>(0, ents[i] + Off.EntityPosition);
                rd[idx].AddValueEntry<ulong>(1, ents[i] + Off.PrefabMgrVic);
                rd[idx].AddValueEntry<ulong>(2, ents[i] + Off.FactionComponent);
                rd[idx].Completed += (_, cb) =>
                {
                    cb.TryGetValue<Vector3f>(0, out posTmp[idx]);
                    cb.TryGetValue<ulong>(1, out prefab[idx]);
                    cb.TryGetValue<ulong>(2, out facComp[idx]);
                };
            }
            map.Execute();

            var outVehicles = new List<VehicleDto>(128);
            var outParts    = new List<VehiclePartDto>(64);

            // Resolve faction/type/name
            for (int i = 0; i < ents.Length; i++)
            {
                ulong e = ents[i]; if (e == 0) continue;

                // distance cull
                float dx = posTmp[i].X - cam.X, dy = posTmp[i].Y - cam.Y, dz = posTmp[i].Z - cam.Z;
                float dsq = dx*dx + dy*dy + dz*dz;
                if (dsq > maxSq) continue;
                int dist = (int)MathF.Sqrt(dsq);

                // prefab
                string typePath = TryReadPrefabType(prefab[i]);   // raw path
                string name     = TryReadPrefabName(prefab[i]);   // nice from path
                string faction  = TryReadFaction(facComp[i]);

                // robust fallbacks
                string rtti = DmaMemory.Rtti.ReadRtti(e) ?? "";
                if (string.IsNullOrWhiteSpace(typePath)) typePath = rtti;
                if (string.IsNullOrWhiteSpace(name)) name = NiceFromPath(typePath) ?? NiceFromType(typePath);

                // parts go to debug list only
                if (IsVehiclePart(typePath, name, rtti))
                {
                    outParts.Add(new VehiclePartDto(e, typePath ?? "", name ?? "", posTmp[i], dist));
                    continue;
                }

                // vehicle classification
                if (!LooksLikeVehicle(typePath, name, e)) continue;

                var anchor = NudgeTowardCamera(posTmp[i], 3.0f);
                outVehicles.Add(new VehicleDto(e, typePath ?? "", name ?? "", faction ?? "", anchor, dist));
            }

            // sort by distance; limit to a sensible amount
            LatestVehicleParts = outParts.OrderBy(p => p.Distance).Take(512).ToArray();
            return outVehicles.OrderBy(v => v.Distance).Take(512).ToArray();
        }

        private static ItemDto[] ScanItems()
        {
            if (!TryGetWorld(out ulong gw)) return Array.Empty<ItemDto>();

            if (!DmaMemory.Read(gw + Off.EntityList, out ulong list) || list == 0) return Array.Empty<ItemDto>();
            if (!DmaMemory.Read(gw + Off.EntityCount, out int count) || count <= 0) return Array.Empty<ItemDto>();

            int take = Math.Min(count, MaxScan);
            var ents = ReadEntityArray(list, count, take);
            if (ents.Length == 0) return Array.Empty<ItemDto>();

            Game.UpdateCamera();
            var cam = Game.Camera.Position;
            float maxSq = MaxDrawDistance * MaxDrawDistance;

            var outList = new List<ItemDto>(256);

            using var map = DmaMemory.Scatter();
            var rd = map.AddRound(useCache: true);

            var posTmp  = new Vector3f[ents.Length];
            var prefab  = new ulong[ents.Length];

            for (int i = 0; i < ents.Length; i++)
            {
                int idx = i;
                rd[idx].AddValueEntry<Vector3f>(0, ents[i] + Off.EntityPosition);
                rd[idx].AddValueEntry<ulong>(1, ents[i] + Off.PrefabMgrVic);
                rd[idx].Completed += (_, cb) =>
                {
                    cb.TryGetValue<Vector3f>(0, out posTmp[idx]);
                    cb.TryGetValue<ulong>(1, out prefab[idx]);
                };
            }
            map.Execute();

            for (int i = 0; i < ents.Length; i++)
            {
                // distance cull
                float dx = posTmp[i].X - cam.X, dy = posTmp[i].Y - cam.Y, dz = posTmp[i].Z - cam.Z;
                float dsq = dx*dx + dy*dy + dz*dz;
                if (dsq > maxSq) continue;
                int dist = (int)MathF.Sqrt(dsq);

                string typePath = TryReadPrefabType(prefab[i]);
                string name     = TryReadPrefabName(prefab[i]);

                var kind = ClassifyItemKind(typePath, name);
                if (kind == null) continue;

                if (string.IsNullOrWhiteSpace(name)) name = NiceFromPath(typePath) ?? NiceFromType(typePath);

                outList.Add(new ItemDto(ents[i], kind, typePath ?? "", name ?? "", posTmp[i], dist));
            }

            // Optional: you can sort by distance here (already fine)
            return outList
                .OrderBy(i => i.Distance)
                .Take(1024)
                .ToArray();
        }

        // ===================== HELPERS =====================
        private static bool TryGetWorld(out ulong gw)
        {
            gw = 0;
            if (!DmaMemory.Read(DmaMemory.Base + Off.Game, out ulong game) || game == 0) return false;
            if (!DmaMemory.Read(game + Off.GameWorld, out gw) || gw == 0) return false;
            return true;
        }

        private static ulong[] ReadEntityArray(ulong list, int count, int cap)
        {
            if (count <= cap)
                return DmaMemory.ReadArray<ulong>(list, count) ?? Array.Empty<ulong>();

            // stride-sample if huge
            int take = cap;
            var ents = new ulong[take];
            double stride = (double)count / take;

            using var samp = DmaMemory.Scatter();
            var r = samp.AddRound(useCache: false);
            for (int i = 0; i < take; i++)
            {
                int srcIndex = (int)(i * stride);
                int idx = i;
                r[idx].AddValueEntry<ulong>(0, list + (ulong)(srcIndex * 8));
                r[idx].Completed += (_, cb) => cb.TryGetValue<ulong>(0, out ents[idx]);
            }
            samp.Execute();
            return ents;
        }

        // == Prefab readers ==
        // Prefab “type” path via PrefabMgr -> DataClass -> TypeString
        private static string TryReadPrefabType(ulong prefabMgr)
        {
            if (prefabMgr == 0) return null;
            if (!DmaMemory.Read(prefabMgr + Off.PrefabDataClassVic, out ulong cls) || cls == 0) return null;

            // ❶ First: real prefab/model path via Class + Off.PrefabModelNamePtr → char*
            if (DmaMemory.Read(cls + Off.PrefabModelNamePtr, out ulong pathPtr) && pathPtr != 0)
            {
                var s = DmaMemory.ReadString(pathPtr, 256, Encoding.ASCII);
                if (!string.IsNullOrWhiteSpace(s)) return s; // e.g. "Vehicle/Car/HMMWV.et"
            }

            // ❷ Fallback: RTTI-ish string (often "VehicleClass")
            if (DmaMemory.Read(cls + Off.PrefabDataType, out ulong typePtr) && typePtr != 0)
            {
                var s = DmaMemory.ReadString(typePtr, 128, Encoding.ASCII);
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }

            // ❸ Last resort: your resolver cache
            return DmaMemory.TryGetPath(cls, out var path) ? path : null;
        }

        private static string TryReadPrefabName(ulong prefabMgr)
        {
            // Human label from the path if possible
            var path = TryReadPrefabType(prefabMgr);
            return NiceFromPath(path);
        }

        // Optional tester mirroring your C++ EntityName2 (entity → prefab path)
        public static string TryReadEntityPrefabPath(ulong entity)
        {
            if (!DmaMemory.Read(entity + Off.PrefabMgrVic, out ulong pm) || pm == 0) return null;
            return TryReadPrefabType(pm);
        }

        private static string TryReadFaction(ulong factionComp)
        {
            if (factionComp == 0) return null;

            // factionComp -> DataClass -> DataType (string ptr)
            if (!DmaMemory.Read(factionComp + Off.FactionComponentDataClass, out ulong fClass) || fClass == 0)
                return null;

            if (!DmaMemory.Read(fClass + Off.FactionComponentDataType, out ulong fType) || fType == 0)
                return null;

            return DmaMemory.ReadString(fType, FactionStringMax, Encoding.ASCII);
        }

        private static string NiceFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            // Take the filename (…/Foo/Bar/HMMWV.et) -> "HMMWV"
            int slash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            string file = slash >= 0 ? path[(slash + 1)..] : path;

            int dot = file.LastIndexOf('.');
            if (dot > 0) file = file[..dot];

            // Replace underscores & dashes with spaces, title-case-ish
            file = file.Replace('_', ' ').Replace('-', ' ');
            if (file.Length == 0) return null;
            if (file.Length <= 1) return file.ToUpperInvariant();

            return char.ToUpperInvariant(file[0]) + file[1..];
        }

        public static string NiceFromType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return null;

            // Strip common suffixes
            string s = type;
            s = s.Replace("Class", "", StringComparison.OrdinalIgnoreCase);
            s = s.Replace("Component", "", StringComparison.OrdinalIgnoreCase);
            s = s.Replace("Entity", "", StringComparison.OrdinalIgnoreCase);
            s = s.Replace("SCR_", "", StringComparison.OrdinalIgnoreCase);
            s = s.Trim();

            return s.Length == 0 ? type : s;
        }

        // Very broad “vehicle” heuristic (mod-friendly)
        private static bool LooksLikeVehicle(string type, string name, ulong entity)
        {
            string t = type ?? "";
            string n = name ?? "";
            string r = DmaMemory.Rtti.ReadRtti(entity) ?? "";

            // Hard negatives first
            if (HasAny(t, NotVehicleTokens) || HasAny(n, NotVehicleTokens) || HasAny(r, NotVehicleTokens))
                return false;

            // Prefer path-based positives (mods typically include "/Vehicle/.../Foo.et")
            if (t.IndexOf("Vehicle/", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // Otherwise accept common vehicle terms (but avoid generic "Vehicle" alone)
            if (HasAny(t, VehiclePositiveTokens) || HasAny(n, VehiclePositiveTokens) || HasAny(r, VehiclePositiveTokens))
                return true;

            return false;
        }

        private static bool IsVehiclePart(string type, string name, string rtti)
        {
            string t = type ?? "", n = name ?? "", r = rtti ?? "";

            // Anything with these tokens is NOT a whole vehicle
            if (HasAny(t, NotVehicleTokens) || HasAny(n, NotVehicleTokens) || HasAny(r, NotVehicleTokens))
                return true;

            // user override: if display name explicitly says Part
            if (n.IndexOf("Part", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            return false;
        }

        // Return "Weapon", "Magazine", or "Item" (or null to skip)
        private static string ClassifyItemKind(string type, string name)
        {
            string t = type ?? "";
            string n = name ?? "";

            // magazines first (avoid weapons with “mag” somewhere)
            if (ContainsAny(t, "Magazine", "Mag", "Clip") || ContainsAny(n, "Magazine", "Mag", "Clip"))
                return "Magazine";

            // weapons
            if (ContainsAny(t, "Weapon", "Rifle", "Pistol", "SMG", "MG", "Launcher", "Grenade", "Shotgun") ||
                ContainsAny(n, "Rifle", "Pistol", "SMG", "MG", "Launcher", "Grenade", "Shotgun"))
                return "Weapon";

            // optionally include generic pickup items:
            if (ContainsAny(t, "Item", "Ammo") || ContainsAny(n, "Ammo"))
                return "Item";

            return null;
        }

        private static bool ContainsAny(string s, params string[] tokens)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var tok in tokens)
                if (s.IndexOf(tok, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        // Stuff that is NOT a whole/drivable vehicle
        private static readonly string[] NotVehicleTokens =
        {
            // lights / reflectors / indicators
            "VehicleLight","Light","Indicator","Reflector","LIndicator","RIndicator",
            "Brakelight","Headlight","HiBeam","Hazard","Dome","Cockpit","Searchlight",
            "Navigating","AntiCollision","Landing", "rear", "gunship",

            // supply/props that happen to include "Vehicle"
            "SupplyCrate","SupplyStack","SupplyPortableContainers","Supply","CanvasTruck",

            // generic parts
            "VehiclePart","Wheel","Door","Hatch","Turret","Seat","Chassis","Hull","Track","Mount","Rotor", "ETool", "Vest",

            // extra noisy bits seen in your logs
            "shadow","glass","window","interior","decal","roof","canvas","bench","cargo","floor"
        };

        // Obvious vehicle positives (keep)
        private static readonly string[] VehiclePositiveTokens =
        {
            "Vehicle/", "Car","Truck","Tank","APC","IFV","MBT",
            "BTR","BMP","BRDM","HMMWV","Humvee","UAZ","Ural",
            "Heli","Helicopter","Mi8","UH1H Int","Plane","Jet","Aircraft","Boat","Ship",
            "VehicleWheeled","VehicleCar", "M1025", "M151A2", "M923", "LAV", "URAL" // NEW
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasAny(string s, string[] toks)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < toks.Length; i++)
                if (s.IndexOf(toks[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static int ScoreVehicleName(in string name, in string typePath)
        {
            int s = 0;
            var n = name ?? "";
            var t = typePath ?? "";
            if (t.IndexOf("Vehicle/", StringComparison.OrdinalIgnoreCase) >= 0) s += 40;
            // exact “NiceFromPath(type)” match usually means the real root prefab
            var niceFromType = NiceFromPath(t);
            if (!string.IsNullOrEmpty(niceFromType) &&
                n.Equals(niceFromType, StringComparison.OrdinalIgnoreCase)) s += 30;

            if (HasAny(n, NotVehicleTokens) || HasAny(t, NotVehicleTokens)) s -= 60;


            // shorter, cleaner names tend to be roots (“BTR70” vs “BTR70 window FL1”)
            if (n.Length <= 12) s += 12;
            else if (n.Length <= 18) s += 6;

            return s;
        }

        // ===================== DEBUG =====================
        // Quick sanity log you can call from a button/hotkey.
        public static void DebugDumpVehicleNames(int max = 50)
        {
            var meta = RescanVehiclesMeta();  // names first, filtered
            int take = Math.Min(max, meta.Length);

            // read positions for the filtered set
            var pos = new Vector3f[take];
            using (var sc = DmaMemory.Scatter())
            {
                var r = sc.AddRound(useCache: false);
                for (int i = 0; i < take; i++)
                {
                    int idx = i;
                    r[idx].AddValueEntry<Vector3f>(0, meta[i].Ptr + Off.EntityPosition);
                    r[idx].Completed += (_, cb) => cb.TryGetValue<Vector3f>(0, out pos[idx]);
                }
                sc.Execute();
            }

            Game.UpdateCamera();
            var cam = Game.Camera.Position;

            Console.WriteLine($"[VehicleNames] {take}/{meta.Length}");
            for (int i = 0; i < take; i++)
            {
                float dx = pos[i].X - cam.X, dy = pos[i].Y - cam.Y, dz = pos[i].Z - cam.Z;
                int dist = (int)MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                Console.WriteLine($"  {dist,4} m  {meta[i].Name}  <{meta[i].Type}>  ptr=0x{meta[i].Ptr:X}");
            }
        }
    }
}
