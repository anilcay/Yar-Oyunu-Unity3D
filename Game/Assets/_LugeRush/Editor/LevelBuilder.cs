using System.Collections.Generic;
using System.IO;
using LugeRush.Track;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace LugeRush.Editor
{
    /// <summary>
    /// Regenerates the three race maps from scratch: track definition, terrain, scatter, set pieces, atmosphere.
    /// Same seed, same map. Batch: -executeMethod LugeRush.Editor.LevelBuilder.BuildAll (or BuildForest/Alpine/Canyon).
    /// </summary>
    public static class LevelBuilder
    {
        public const string LevelArt = EditorAssetUtil.Root + "/Art/Materials/Level";
        private const string SceneFolder = EditorAssetUtil.Root + "/Scenes";
        private const string PropFolder = EditorAssetUtil.Root + "/ScriptableObjects/Tracks";

        [MenuItem("Luge Rush/Levels/Build All")]
        public static void BuildAll()
        {
            BuildForest();
            BuildAlpine();
            BuildCanyon();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Luge Rush/Levels/Build Forest")] public static void BuildForest() => Build(LevelTracks.Forest());
        [MenuItem("Luge Rush/Levels/Build Alpine")] public static void BuildAlpine() => Build(LevelTracks.Alpine());
        [MenuItem("Luge Rush/Levels/Build Canyon")] public static void BuildCanyon() => Build(LevelTracks.Canyon());

        private static void Build(LevelLayout layout)
        {
            var ctx = new LevelContext { Layout = layout, TerrainParams = TerrainFor(layout), CurbWidth = layout.Definition.CurbWidth };
            var options = new TrackSceneBuilder.Options
            {
                CustomEnvironment = true,
                FinishArchOnLapLine = true,
                GridFirstRowOffset = LevelTracks.GridFirstRowOffset,
                TrackReady = path => { ctx.Path = path; Plan(ctx); },
                EdgeFilter = (d, side) => EdgeAllowed(ctx, d, side),
                SignFilter = d => !ctx.IsCovered(d),
                PostTrack = (path, builder, root) => Dress(ctx, builder, root),
            };
            string scene = $"{SceneFolder}/{layout.SceneName}.unity";
            TrackSceneBuilder.BuildScene(layout.Definition, scene, options);
            AssetDatabase.SaveAssets();
            Report(ctx);
        }

        private static void Plan(LevelContext c)
        {
            c.Field = new LevelTrackField(c.Path, c.CurbWidth);
            LevelSetPieces.PlanCommon(c);
            switch (c.Layout.Biome)
            {
                case LevelBiome.Forest: LevelSetPieces.PlanForest(c); break;
                case LevelBiome.Alpine: LevelSetPieces.PlanAlpine(c); break;
                default: LevelSetPieces.PlanCanyon(c); break;
            }
        }

        /// <summary>Rails on the outside of curves and in rail zones; nothing in covered sections.</summary>
        private static bool EdgeAllowed(LevelContext c, float d, int side)
        {
            if (c.IsCovered(d)) return false;
            if (c.InRailZone(d)) return true;
            float k = 0f;
            for (float o = -6f; o <= 6f; o += 3f)
            {
                float v = c.Path.GetCurvature(d + o);
                if (Mathf.Abs(v) > Mathf.Abs(k)) k = v;
            }
            // Outer side of a right turn (k > 0) is the left edge.
            return Mathf.Abs(k) > 0.012f && (k > 0f ? side < 0 : side > 0);
        }

        private static void Dress(LevelContext c, TrackMeshBuilder builder, Transform root)
        {
            var env = new GameObject("Environment").transform;
            c.Root = env;
            // Thin kit parts barely show a shadow, but would be drawn again in every cascade.
            foreach (string part in new[] { "Guardrails", "Signs" })
            {
                var t = root.Find(part);
                if (t == null) continue;
                foreach (var mr in t.GetComponentsInChildren<MeshRenderer>()) mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            var biome = c.Layout.Biome;

            var terrain = new LevelTerrain(c.Field, c.TerrainParams);
            c.Terrain = terrain;
            terrain.Generate();
            var palette = AssetDatabase.LoadAssetAtPath<Material>(ArtMaterialFactory.Palette);
            terrain.BuildObjects(env, $"{LevelArt}/Meshes", $"Terrain_{biome}", palette, 8);
            terrain.Validate(out int above, out int notHoled);
            Debug.Log($"[LevelBuilder] {biome} terrain: {terrain.VertexPointCount} points, {terrain.TriangleCount} tris, above road {above}, road not holed {notHoled}.");

            var scatter = new LevelScatter(c.Field, terrain, c.Layout.Seed + 1, c.Exclusions, k => c.IsCovered(k * LevelTrackField.Step));
            ScatterRules(c, scatter);
            var props = scatter.Save($"{PropFolder}/Props_{biome}.asset");
            var propGo = new GameObject("Props");
            propGo.transform.SetParent(env, false);
            propGo.AddComponent<LevelPropRenderer>().Data = props;

            LevelSetPieces.PlaceCommon(c);
            switch (biome)
            {
                case LevelBiome.Forest:
                    LevelSetPieces.PlaceForest(c);
                    LevelSetPieces.PlaceCards(c, new[] { "Common/Env_Common_MountainCard_02", "Common/Env_Common_MountainCard_01" }, 9, 760f, -10f, 1f, 1.5f, c.Layout.Seed);
                    break;
                case LevelBiome.Alpine:
                    LevelSetPieces.PlaceAlpine(c);
                    LevelSetPieces.PlaceCards(c, new[] { "Alpine/Env_Alpine_SnowPeaks_01", "Alpine/Env_Alpine_SnowPeaks_02", "Common/Env_Common_MountainCard_03" }, 11, 980f, -30f, 1.1f, 1.7f, c.Layout.Seed);
                    break;
                default:
                    LevelSetPieces.PlaceCanyon(c);
                    LevelSetPieces.PlaceCards(c, new[] { "Canyon/Env_Canyon_MesaCard_01" }, 11, 880f, -20f, 1f, 1.5f, c.Layout.Seed);
                    break;
            }
            LevelSky.Build(c, env);
            AddViewpoints(c, env);
        }

        /// <summary>Review cameras: the landmarks, the start compound and the lift (station pulley side check).</summary>
        private static void AddViewpoints(LevelContext c, Transform env)
        {
            var vp = new GameObject("Viewpoints").AddComponent<LevelViewpoints>();
            vp.transform.SetParent(env, false);
            var path = c.Path;
            void Road(string name, float d, float ahead = 30f, float lateral = 0f, float height = 1.9f, float fov = 65f)
            {
                Vector3 p = path.GetPosition(d) + c.Flat(path.GetRight(d)) * lateral + Vector3.up * height;
                vp.Points.Add(new LevelViewpoints.Viewpoint { Name = name, Position = p, Target = path.GetPosition(d + ahead) + Vector3.up * 1.2f, Fov = fov });
            }
            float d0 = path.StartLineDistance;
            Road("start", d0 - 12f, 20f, 1f, 1.8f, 62f);
            Road("lift", path.LiftStart - 9f, 24f, 2.2f, 2.6f, 62f);
            Road("lifttop", path.LiftEnd - 10f, 8f, -3.5f, 3.4f, 60f);
            switch (c.Layout.Biome)
            {
                case LevelBiome.Forest:
                    Road("setpiece", c.D("bridge") - 32f, 34f);
                    Road("tunnel", c.D("tunnel_a") - 14f, 22f);
                    Road("hairpin", c.D("hairpin1_in") - 6f, 26f, 0f, 3.5f, 75f);
                    Road("view", c.D("view") - 28f, 30f, 0f, 2.2f);
                    Road("banked", c.D("banked_out") - 45f, 28f);
                    break;
                case LevelBiome.Alpine:
                    Road("setpiece", c.D("waterfall") - 22f, 26f, 1f, 2.2f, 70f);
                    Road("stream", c.D("waterfall") + 2f, 18f, -1f, 3.2f, 70f);
                    Road("tunnel", c.D("tunnel_a") - 20f, 22f, 0f, 2.2f);
                    Road("panorama", c.D("panorama") - 55f, 32f, 0f, 2.2f, 75f);
                    Road("hairpin", c.D("hairpin_in") - 6f, 26f, 0f, 3.5f, 75f);
                    Road("wide", c.D("wide_out") - 90f, 32f, 0f, 2.2f, 75f);
                    break;
                default:
                    Road("setpiece", c.D("arch") - 34f, 36f, 0f, 2.2f, 65f);
                    Road("gorge", c.D("gorge_a") - 14f, 40f, 0f, 2.2f, 65f);
                    Road("shed", c.D("shed_a") - 12f, 20f);
                    Road("mine", c.D("mine") - 44f, 34f, 0f, 2.2f, 70f);
                    Road("hairpin", c.D("hairpin_in") - 6f, 26f, 0f, 3.5f, 75f);
                    break;
            }
        }

        // ---------- Terrain recipes ----------

        private static LevelTerrainParams TerrainFor(LevelLayout l)
        {
            switch (l.Biome)
            {
                case LevelBiome.Forest:
                    return new LevelTerrainParams
                    {
                        Seed = l.Seed, DefaultBlend = 8f, DefaultLeft = 3f, DefaultRight = 3f, DefaultRange = 40f,
                        FarRise = 55f, BorderRise = 70f, NoiseScale = 0.8f, Paint = PaintForest
                    };
                case LevelBiome.Alpine:
                    return new LevelTerrainParams
                    {
                        Seed = l.Seed, DefaultBlend = 9f, DefaultLeft = 5f, DefaultRight = 5f, DefaultRange = 50f,
                        FarRise = 75f, BorderRise = 110f, NoiseScale = 1.2f, Paint = PaintAlpine
                    };
                default:
                    return new LevelTerrainParams
                    {
                        Seed = l.Seed, DefaultBlend = 7f, DefaultLeft = 12f, DefaultRight = 12f, DefaultRange = 34f,
                        FarRise = 34f, FarRiseStart = 25f, FarRiseEnd = 130f, BorderRise = 45f, NoiseScale = 1f, TerraceStep = 11f, Paint = PaintCanyon
                    };
            }
        }

        private static string Pick(float noise, params string[] cells) => cells[Mathf.Clamp((int)(noise * cells.Length), 0, cells.Length - 1)];

        private static string PaintForest(float h, float slope, float noise, float edge)
        {
            if (edge < 1.4f) return Pick(noise, "soil_base", "soil_dark", "moss", "soil_light");
            if (slope > 0.22f) return Pick(noise, "soil_dark", "moss", "soil_base", "leaf_dark_dark");
            if (edge < 16f) return Pick(noise, "leaf_mid_dark", "fern", "moss", "leaf_dark_base", "leaf_mid_base", "leaf_bright_dark");
            return Pick(noise, "leaf_dark_base", "leaf_mid_dark", "leaf_dark_dark", "leaf_mid_base", "fern");
        }

        private static string PaintAlpine(float h, float slope, float noise, float edge)
        {
            if (edge < 1.2f) return Pick(noise, "soil_base", "alpine_grass_dark", "granite_dark");
            if (h > 150f + noise * 50f) return slope > 0.42f ? "granite_cool" : Pick(noise, "snow_base", "snow_light", "snow_dark");
            if (slope > 0.3f) return Pick(noise, "granite_base", "granite_dark", "granite_cool", "granite_warm");
            if (h > 105f + noise * 30f) return Pick(noise, "alpine_grass_cool", "granite_light", "alpine_grass_dark");
            return Pick(noise, "alpine_grass_dark", "alpine_grass_base", "alpine_grass_cool", "alpine_grass_warm", "alpine_grass_light");
        }

        private static readonly string[] CanyonBands = { "canyon_rock_base", "canyon_orange_dark", "canyon_rock_dark", "canyon_rock_warm", "canyon_orange_base", "canyon_rock_cool", "rock_deep" };

        private static string PaintCanyon(float h, float slope, float noise, float edge)
        {
            if (edge < 1.3f) return Pick(noise, "sand_dark", "sand_base", "canyon_orange_light");
            if (slope < 0.1f) return Pick(noise, "sand_base", "sand_dark", "canyon_orange_light", "canyon_orange_base");
            if (slope < 0.2f && edge < 30f) return Pick(noise, "sand_dark", "sand_deep", "canyon_orange_dark");
            return CanyonBands[Mathf.Abs((int)(h / 5.5f)) % CanyonBands.Length];
        }

        // ---------- Scatter recipes ----------

        private static string[] Names(string folder, string stem, int n)
        {
            var list = new string[n];
            for (int i = 0; i < n; i++) list[i] = $"{folder}/Env_{folder}_{stem}_0{i + 1}";
            return list;
        }

        private static string[] Join(params string[][] sets)
        {
            var all = new List<string>();
            foreach (var s in sets) all.AddRange(s);
            return all.ToArray();
        }

        private static void ScatterRules(LevelContext c, LevelScatter s)
        {
            var rocksSmall = Names("Common", "RockSmall", 4);
            var rocksMed = Names("Common", "RockMedium", 4);
            var rocksLarge = Names("Common", "RockLarge", 4);
            switch (c.Layout.Biome)
            {
                case LevelBiome.Forest:
                {
                    var trees = Names("Forest", "TallTree", 5);
                    s.Apply(new LevelScatterRule { Prefabs = Join(Names("Common", "Grass", 4), Names("Forest", "Fern", 2), Names("Forest", "FlowerBush", 3), rocksSmall), From = 0.2f, To = 2.4f, Density = 0.32f, ScaleMin = 0.8f, ScaleMax = 1.5f, Shadows = false, DrawDistance = 80f, MaxSlope = 0.5f, Sink = 0.03f, Tilt = 6f });
                    s.Apply(new LevelScatterRule { Prefabs = Join(Names("Common", "Grass", 4), Names("Forest", "Fern", 2)), From = 2.4f, To = 30f, Density = 0.06f, ScaleMin = 0.9f, ScaleMax = 1.6f, Shadows = false, DrawDistance = 90f, MaxSlope = 0.5f });
                    s.Apply(new LevelScatterRule { Prefabs = Names("Forest", "Bamboo", 4), From = 1.8f, To = 10f, Density = 0.018f, Cluster = 3, ClusterRadius = 2.2f, ScaleMin = 0.85f, ScaleMax = 1.3f, DrawDistance = 170f, Tall = true, Spacing = 1.6f, SpacingClass = "stem" });
                    s.Apply(new LevelScatterRule { Prefabs = Join(Names("Forest", "BananaPlant", 4), new[] { "Forest/Env_Forest_Fern_03" }), From = 1f, To = 10f, Density = 0.03f, ScaleMin = 0.85f, ScaleMax = 1.3f, DrawDistance = 130f, Tall = true });
                    s.Apply(new LevelScatterRule { Prefabs = Names("Forest", "MossRock", 3), From = 1f, To = 14f, Density = 0.008f, ScaleMin = 0.8f, ScaleMax = 1.3f, DrawDistance = 150f, Tilt = 10f });
                    s.Apply(new LevelScatterRule { Prefabs = trees, From = 3f, To = 34f, Density = 0.03f, Spacing = 4f, SpacingClass = "tree", ScaleMin = 0.8f, ScaleMax = 1.25f, DrawDistance = 300f, Tall = true });
                    s.Apply(new LevelScatterRule { Prefabs = Names("Forest", "GiantTree", 3), From = 7f, To = 24f, Density = 0.004f, Spacing = 9f, SpacingClass = "tree", ScaleMin = 0.9f, ScaleMax = 1.15f, DrawDistance = 340f, Tall = true });
                    s.Apply(new LevelScatterRule { Prefabs = trees, From = 34f, To = 95f, Density = 0.012f, Spacing = 6f, SpacingClass = "tree", ScaleMin = 0.9f, ScaleMax = 1.3f, DrawDistance = 300f, MaxSlope = 0.3f });
                    s.Apply(new LevelScatterRule { Prefabs = trees, From = 80f, To = 420f, Density = 0.0026f, Spacing = 9f, SpacingClass = "tree", ScaleMin = 1f, ScaleMax = 1.45f, DrawDistance = 420f, Shadows = false, MaxSlope = 0.35f });
                    break;
                }
                case LevelBiome.Alpine:
                {
                    var pines = Names("Alpine", "Pine", 4);
                    s.Apply(new LevelScatterRule { Prefabs = Join(Names("Alpine", "Flowers", 3), Names("Alpine", "Grass", 3)), From = 0.2f, To = 8f, Density = 0.16f, Cluster = 3, ClusterRadius = 1.3f, ScaleMin = 0.8f, ScaleMax = 1.5f, Shadows = false, DrawDistance = 90f, MaxSlope = 0.2f, Sink = 0.02f });
                    s.Apply(new LevelScatterRule { Prefabs = Names("Alpine", "Grass", 3), From = 8f, To = 34f, Density = 0.05f, ScaleMin = 1f, ScaleMax = 1.6f, Shadows = false, DrawDistance = 100f, MaxSlope = 0.2f });
                    s.Apply(new LevelScatterRule { Prefabs = Join(rocksSmall, rocksMed), From = 0.4f, To = 14f, Density = 0.01f, ScaleMin = 0.7f, ScaleMax = 1.25f, DrawDistance = 180f, Tilt = 10f, MaxSlope = 0.5f });
                    s.Apply(new LevelScatterRule { Prefabs = rocksLarge, From = 5f, To = 40f, Density = 0.0026f, ScaleMin = 0.7f, ScaleMax = 1.25f, DrawDistance = 340f, Tilt = 10f, MaxSlope = 0.5f });
                    s.Apply(new LevelScatterRule { Prefabs = pines, From = 3.5f, To = 50f, Density = 0.016f, Spacing = 3.5f, SpacingClass = "tree", ScaleMin = 0.7f, ScaleMax = 1.25f, DrawDistance = 520f, Tall = true, MaxSlope = 0.32f, MaxHeight = 125f });
                    s.Apply(new LevelScatterRule { Prefabs = new[] { "Alpine/Env_Alpine_FallenPine_01", "Alpine/Env_Alpine_Cliff_04" }, From = 7f, To = 45f, Density = 0.0005f, ScaleMin = 0.8f, ScaleMax = 1.1f, DrawDistance = 340f, Tilt = 6f, Tall = true, MaxSlope = 0.3f });
                    s.Apply(new LevelScatterRule { Prefabs = pines, From = 50f, To = 98f, Density = 0.008f, Spacing = 5f, SpacingClass = "tree", ScaleMin = 0.8f, ScaleMax = 1.3f, DrawDistance = 700f, Shadows = false, MaxSlope = 0.32f, MaxHeight = 135f });
                    s.Apply(new LevelScatterRule { Prefabs = Join(pines, new[] { "Alpine/Env_Alpine_Pine_05" }), From = 90f, To = 520f, Density = 0.0024f, Spacing = 8f, SpacingClass = "tree", ScaleMin = 1f, ScaleMax = 1.5f, DrawDistance = 900f, Shadows = false, MaxSlope = 0.3f, MaxHeight = 150f });
                    // Cliff wall on the uphill side of the long straight and of the panoramic curve.
                    var cliffs = new[] { "Alpine/Env_Alpine_Cliff_01", "Alpine/Env_Alpine_Cliff_02", "Alpine/Env_Alpine_Cliff_03" };
                    float fall = c.D("waterfall");
                    s.Apply(new LevelRowRule { Prefabs = cliffs, FromDistance = c.D("long_in") + 6f, ToDistance = fall - 12f, Side = -1, Offset = 7f, Step = 8.5f, ScaleMin = 0.95f, ScaleMax = 1.25f });
                    s.Apply(new LevelRowRule { Prefabs = cliffs, FromDistance = fall + 54f, ToDistance = c.D("long_out") - 4f, Side = -1, Offset = 7f, Step = 8.5f, ScaleMin = 0.95f, ScaleMax = 1.25f });
                    float pano = c.D("panorama");
                    s.Apply(new LevelRowRule { Prefabs = cliffs, FromDistance = pano - 90f, ToDistance = pano - 6f, Side = -1, Offset = 7f, Step = 8.5f, ScaleMin = 0.95f, ScaleMax = 1.25f });
                    float tA = c.D("tunnel_a"), tB = c.D("tunnel_b");
                    foreach (int side in new[] { -1, 1 })
                    {
                        s.Apply(new LevelRowRule { Prefabs = cliffs, FromDistance = tA - 18f, ToDistance = tA - 1f, Side = side, Offset = 5f, Step = 7f, UseRoadHeight = true });
                        s.Apply(new LevelRowRule { Prefabs = cliffs, FromDistance = tB + 1f, ToDistance = tB + 18f, Side = side, Offset = 5f, Step = 7f, UseRoadHeight = true });
                    }
                    break;
                }
                default:
                {
                    var saguaro = Names("Canyon", "Saguaro", 3);
                    var mesas = Names("Canyon", "Mesa", 5);
                    var walls = Names("Canyon", "Wall", 4);
                    var rockS = Names("Canyon", "RockSmall", 2);
                    var rockM = Names("Canyon", "RockMedium", 2);
                    s.Apply(new LevelScatterRule { Prefabs = Join(Names("Canyon", "DryGrass", 2), Names("Canyon", "DryBush", 2), new[] { "Canyon/Env_Canyon_Tumbleweed_01" }, rockS, rockM), From = 0.2f, To = 7f, Density = 0.12f, ScaleMin = 0.7f, ScaleMax = 1.4f, Shadows = true, DrawDistance = 130f, MaxSlope = 0.35f, Sink = 0.03f, Tilt = 8f });
                    s.Apply(new LevelScatterRule { Prefabs = Join(Names("Canyon", "DryBush", 2), Names("Canyon", "DryGrass", 2)), From = 7f, To = 40f, Density = 0.018f, ScaleMin = 0.9f, ScaleMax = 1.5f, Shadows = false, DrawDistance = 160f, MaxSlope = 0.2f });
                    s.Apply(new LevelScatterRule { Prefabs = saguaro, From = 2f, To = 18f, Density = 0.009f, Spacing = 3f, SpacingClass = "cactus", ScaleMin = 0.8f, ScaleMax = 1.3f, DrawDistance = 340f, Tall = true, MaxSlope = 0.22f });
                    s.Apply(new LevelScatterRule { Prefabs = Names("Canyon", "PricklyPear", 2), From = 1.5f, To = 16f, Density = 0.012f, ScaleMin = 0.8f, ScaleMax = 1.3f, DrawDistance = 190f, MaxSlope = 0.25f });
                    s.Apply(new LevelScatterRule { Prefabs = Names("Canyon", "RockLarge", 2), From = 4f, To = 44f, Density = 0.003f, ScaleMin = 0.8f, ScaleMax = 1.5f, DrawDistance = 400f, Tilt = 10f, MaxSlope = 0.45f });
                    s.Apply(new LevelScatterRule { Prefabs = saguaro, From = 18f, To = 90f, Density = 0.0012f, Spacing = 8f, SpacingClass = "cactus", ScaleMin = 0.9f, ScaleMax = 1.4f, DrawDistance = 420f, Shadows = false, MaxSlope = 0.2f });
                    s.Apply(new LevelScatterRule { Prefabs = mesas, From = 42f, To = 98f, Density = 0.00024f, Spacing = 35f, SpacingClass = "mesa", ScaleMin = 1.05f, ScaleMax = 1.5f, DrawDistance = 1400f, Tilt = 1f, MaxSlope = 0.5f, Sink = 0.5f });
                    s.Apply(new LevelScatterRule { Prefabs = mesas, From = 90f, To = 600f, Density = 0.000075f, Spacing = 50f, SpacingClass = "mesa", ScaleMin = 1.2f, ScaleMax = 2.4f, DrawDistance = 1400f, Tilt = 1f, MaxSlope = 0.6f, Sink = 1f });

                    // Dark narrow gorge + wall lines along the open straights.
                    float gA = c.D("gorge_a"), gB = c.D("gorge_b");
                    foreach (int side in new[] { -1, 1 })
                        s.Apply(new LevelRowRule { Prefabs = Names("Canyon", "GorgeWall", 2), FromDistance = gA, ToDistance = gB, Side = side, Offset = 2.6f, OffsetJitter = 0.5f, Step = 11.5f, ScaleMin = 1f, ScaleMax = 1.1f, YawJitter = 4f, UseRoadHeight = true, Sink = 0.5f, DrawDistance = 900f });
                    float arch = c.D("arch");
                    foreach (int side in new[] { -1, 1 })
                    {
                        s.Apply(new LevelRowRule { Prefabs = walls, FromDistance = c.D("long2_in") - 2f, ToDistance = arch - 26f, Side = side, Offset = 19f, OffsetJitter = 5f, Step = 11f, DrawDistance = 900f, ClearBehind = 26f });
                        s.Apply(new LevelRowRule { Prefabs = walls, FromDistance = arch + 26f, ToDistance = c.D("hairpin_in"), Side = side, Offset = 19f, OffsetJitter = 5f, Step = 11f, DrawDistance = 900f, ClearBehind = 26f });
                    }
                    break;
                }
            }
        }

        // ---------- Report ----------

        private static void Report(LevelContext c)
        {
            var path = c.Path;
            float rise = path.GetPosition(path.LiftEnd).y - path.GetPosition(path.LiftStart).y;
            var data = AssetDatabase.LoadAssetAtPath<LevelPropData>($"{PropFolder}/Props_{c.Layout.Biome}.asset");
            long tris = 0;
            foreach (var g in data.Groups) tris += g.Mesh.GetIndexCount(0) / 3 * g.Instances.Count;
            Debug.Log($"[LevelBuilder] REPORT {c.Layout.Biome}: length {path.Length:0} m, descent {path.Length - path.LiftLength:0} m, drop {rise:0.0} m, lift {path.LiftLength:0} m, " +
                      $"props {data.InstanceCount} ({data.Groups.Count} meshes, {tris / 1000} k tris total).");
        }
    }

    /// <summary>Sun, sky, ambient, fog, post-processing and (forest) light shafts per map (Art/ENVIRONMENT.md).</summary>
    public static class LevelSky
    {
        private class Recipe
        {
            public Vector3 SunEuler;
            public string SunColor;
            public float SunIntensity, ShadowStrength, ShadowDistance;
            public string SkyTop, SkyHorizon, SkyGround;
            public float SkyExponent;
            public string AmbientSky, AmbientEquator, AmbientGround;
            public string Fog;
            public float FogStart, FogEnd;
            public float Bloom, BloomThreshold, Saturation, Contrast, Temperature, Tint, Vignette;
        }

        private static Color C(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        private static Recipe For(LevelBiome b)
        {
            switch (b)
            {
                case LevelBiome.Forest:
                    return new Recipe
                    {
                        SunEuler = new Vector3(62f, 200f, 0f), SunColor = "#FFF1D6", SunIntensity = 1.6f, ShadowStrength = 0.85f, ShadowDistance = 95f,
                        SkyTop = "#BFD9E8", SkyHorizon = "#DDE8D8", SkyGround = "#B9D6AE", SkyExponent = 0.7f,
                        AmbientSky = "#A9C9B0", AmbientEquator = "#7FA27A", AmbientGround = "#3A4A2E",
                        Fog = "#B9D6AE", FogStart = 25f, FogEnd = 260f,
                        Bloom = 0.3f, BloomThreshold = 1.1f, Saturation = 10f, Contrast = 4f
                    };
                case LevelBiome.Alpine:
                    return new Recipe
                    {
                        SunEuler = new Vector3(24f, 115f, 0f), SunColor = "#FFE6C2", SunIntensity = 1.8f, ShadowStrength = 0.9f, ShadowDistance = 110f,
                        SkyTop = "#4F95D6", SkyHorizon = "#CFE3F2", SkyGround = "#CFE0EE", SkyExponent = 0.55f,
                        AmbientSky = "#A8C8E8", AmbientEquator = "#B9C8B0", AmbientGround = "#5A6A50",
                        Fog = "#CFE0EE", FogStart = 60f, FogEnd = 1100f,
                        Bloom = 0.2f, BloomThreshold = 1.1f, Contrast = 10f, Temperature = -5f
                    };
                default:
                    return new Recipe
                    {
                        SunEuler = new Vector3(13f, 250f, 0f), SunColor = "#FFB070", SunIntensity = 2.0f, ShadowStrength = 1f, ShadowDistance = 120f,
                        SkyTop = "#5A3F8E", SkyHorizon = "#F0A060", SkyGround = "#D58C86", SkyExponent = 0.42f,
                        AmbientSky = "#8A6A9E", AmbientEquator = "#E09A70", AmbientGround = "#6A3A30",
                        Fog = "#D58C86", FogStart = 40f, FogEnd = 780f,
                        Bloom = 0.4f, BloomThreshold = 1.05f, Temperature = 15f, Tint = 5f, Vignette = 0.25f
                    };
            }
        }

        public static void Build(LevelContext c, Transform env)
        {
            var biome = c.Layout.Biome;
            var r = For(biome);

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(env, false);
            sunGo.transform.rotation = Quaternion.Euler(r.SunEuler);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = C(r.SunColor);
            sun.intensity = r.SunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = r.ShadowStrength;
            RenderSettings.sun = sun;

            EditorAssetUtil.EnsureFolder(LevelBuilder.LevelArt);
            string skyPath = $"{LevelBuilder.LevelArt}/Sky_{biome}.mat";
            var skyShader = AssetDatabase.LoadAssetAtPath<Shader>($"{LevelBuilder.LevelArt}/LevelGradientSky.shader");
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null)
            {
                sky = new Material(skyShader);
                AssetDatabase.CreateAsset(sky, skyPath);
            }
            sky.shader = skyShader;
            sky.SetColor("_TopColor", C(r.SkyTop));
            sky.SetColor("_HorizonColor", C(r.SkyHorizon));
            sky.SetColor("_GroundColor", C(r.SkyGround));
            sky.SetFloat("_Exponent", r.SkyExponent);
            Vector3 toSun = -sunGo.transform.forward;
            sky.SetVector("_SunDirection", new Vector4(toSun.x, toSun.y, toSun.z, 0f));
            sky.SetColor("_SunColor", C(r.SunColor));
            EditorUtility.SetDirty(sky);

            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = C(r.AmbientSky);
            RenderSettings.ambientEquatorColor = C(r.AmbientEquator);
            RenderSettings.ambientGroundColor = C(r.AmbientGround);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = C(r.Fog);
            RenderSettings.fogStartDistance = r.FogStart;
            RenderSettings.fogEndDistance = r.FogEnd;

            var volumeGo = new GameObject("GlobalVolume");
            volumeGo.transform.SetParent(env, false);
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = BuildProfile(biome, r);

            var atm = volumeGo.AddComponent<LevelAtmosphere>();
            atm.ShadowDistance = r.ShadowDistance;

            if (biome == LevelBiome.Forest) BuildLightShafts(c, env, toSun);
        }

        private static VolumeProfile BuildProfile(LevelBiome biome, Recipe r)
        {
            string path = $"{LevelBuilder.LevelArt}/VP_{biome}.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.value = r.Bloom;
            bloom.threshold.value = r.BloomThreshold;
            var color = profile.Add<ColorAdjustments>(true);
            color.saturation.value = r.Saturation;
            color.contrast.value = r.Contrast;
            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.value = r.Temperature;
            wb.tint.value = r.Tint;
            var tonemap = profile.Add<Tonemapping>(true);
            tonemap.mode.value = TonemappingMode.Neutral;
            if (r.Vignette > 0f)
            {
                var v = profile.Add<Vignette>(true);
                v.intensity.value = r.Vignette;
            }
            foreach (var comp in profile.components)
            {
                comp.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(comp, profile);
            }
            EditorUtility.SetDirty(profile);
            return profile;
        }

        /// <summary>Slanted additive cards along the sun direction next to the road.</summary>
        private static void BuildLightShafts(LevelContext c, Transform env, Vector3 toSun)
        {
            var rng = new System.Random(c.Layout.Seed + 7);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int k = 8; k < c.Field.Count; k += 13)
            {
                float d = k * LevelTrackField.Step;
                if (c.Path.IsInLift(d) || c.IsCovered(d)) continue;
                int side = rng.Next(2) * 2 - 1;
                Vector3 baseP = c.Beside(d, side * (c.Edge(d) + R(1f, 16f))) + Vector3.up * 0.2f;
                baseP += c.Flat(c.Path.GetTangent(d)) * R(-4f, 4f);
                Vector3 across = Vector3.Cross(toSun, c.Flat(c.Path.GetTangent(d))).normalized;
                float halfW = R(1f, 2.6f), len = R(24f, 34f);
                int s = verts.Count;
                Vector3 top = baseP + toSun * len;
                verts.Add(top - across * halfW); uvs.Add(new Vector2(0f, 0f));
                verts.Add(top + across * halfW); uvs.Add(new Vector2(1f, 0f));
                verts.Add(baseP - across * halfW * 1.5f); uvs.Add(new Vector2(0f, 1f));
                verts.Add(baseP + across * halfW * 1.5f); uvs.Add(new Vector2(1f, 1f));
                tris.AddRange(new[] { s, s + 1, s + 2, s + 2, s + 1, s + 3 });
            }
            var mesh = new Mesh { name = "LightShafts_Forest" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh = EditorAssetUtil.SaveMesh(mesh, $"{LevelBuilder.LevelArt}/Meshes", mesh.name);

            string matPath = $"{LevelBuilder.LevelArt}/M_LightShaft.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(AssetDatabase.LoadAssetAtPath<Shader>($"{LevelBuilder.LevelArt}/LevelLightShaft.shader"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetColor("_Color", new Color(1f, 0.95f, 0.72f, 0.1f));

            var go = new GameObject("LightShafts");
            go.transform.SetParent(env, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
    }
}
