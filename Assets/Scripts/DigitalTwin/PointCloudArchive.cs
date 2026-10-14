using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DigitalTwin
{
    /// <summary>存档中的一个缺陷：环向定位参数 + 拍到的帧图片。</summary>
    public class DefectSaveData
    {
        public float angleDeg;    // 缺陷环向角度（0~360）
        public float rangeDeg;    // 缺陷环向角宽
        public int markCount;     // 标红的截面数
        public int startSection;  // 缺陷起始截面索引
        public string typeName;   // 缺陷类型（如「腐蚀」「划痕」）
        public readonly List<Texture2D> frames = new List<Texture2D>();
    }

    /// <summary>存档元数据（写入 meta.json）。</summary>
    [Serializable]
    public class ArchiveMeta
    {
        public string name;
        public string createdAt;
        public int sectionSize;
        public int sectionCount;
        public float avgRadius;
        public List<DefectEntry> defects = new List<DefectEntry>();
    }

    /// <summary>meta.json 中的单个缺陷条目。</summary>
    [Serializable]
    public class DefectEntry
    {
        public float angleDeg;
        public float rangeDeg;
        public int markCount;
        public int startSection;
        public string typeName;   // 缺陷类型（如「腐蚀」「划痕」）
        public List<string> images = new List<string>(); // images/ 下的文件名
    }

    /// <summary>读取存档后的完整数据。</summary>
    public class ArchiveData
    {
        public ArchiveMeta meta;
        public Vector3[][] sections;
        public readonly List<DefectSaveData> defects = new List<DefectSaveData>();
    }

    /// <summary>
    /// 点云建模本地存档：把累积的截面环（世界坐标）写成二进制，缺陷元数据写成 JSON，
    /// 缺陷帧图片写成 PNG，统一放在 Application.persistentDataPath 下的时间戳文件夹里。
    /// 目录结构：
    ///   PointCloudSaves/2026-10-11_14-30-00/
    ///       model.bin      截面环顶点（二进制）
    ///       meta.json      元数据 + 缺陷定位参数
    ///       images/        缺陷帧图片
    /// </summary>
    public static class PointCloudArchive
    {
        private const string RootFolder = "PointCloudSaves";
        private const string ModelFile = "model.bin";
        private const string MetaFile = "meta.json";
        private const string ImageFolder = "images";
        private const uint Magic = 0x50435356; // "PCSV"
        private const int Version = 1;

        /// <summary>存档根目录。</summary>
        public static string RootDir => Path.Combine(Application.persistentDataPath, RootFolder);

        /// <summary>列出所有存档目录，按名称倒序（最新在前）。</summary>
        public static List<string> ListArchives()
        {
            List<string> list = new List<string>();
            try
            {
                if (!Directory.Exists(RootDir))
                {
                    return list;
                }
                foreach (string dir in Directory.GetDirectories(RootDir))
                {
                    if (File.Exists(Path.Combine(dir, MetaFile)))
                    {
                        list.Add(dir);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[PointCloudArchive] 列目录失败：{e.Message}");
            }

            list.Sort((a, b) => string.CompareOrdinal(Path.GetFileName(b), Path.GetFileName(a)));
            return list;
        }

        /// <summary>读取一个存档的元数据（meta.json）。</summary>
        public static ArchiveMeta ReadMeta(string dir)
        {
            try
            {
                string metaPath = Path.Combine(dir, MetaFile);
                if (!File.Exists(metaPath))
                {
                    return null;
                }
                return JsonUtility.FromJson<ArchiveMeta>(File.ReadAllText(metaPath));
            }
            catch (Exception e)
            {
                Debug.LogError($"[PointCloudArchive] 读取元数据失败：{e.Message}");
                return null;
            }
        }

        /// <summary>删除一个本地存档（含 model.bin / meta.json / images），成功返回 true。</summary>
        public static bool Delete(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    return false;
                }
                Directory.Delete(dir, true);
                Debug.Log($"[PointCloudArchive] 已删除存档：{dir}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[PointCloudArchive] 删除失败：{e.Message}");
                return false;
            }
        }

        /// <summary>保存一次建模，成功返回存档目录，失败返回 null。</summary>
        public static string Save(Vector3[][] sections, float avgRadius, List<DefectSaveData> defects)
        {
            if (sections == null || sections.Length == 0)
            {
                return null;
            }

            try
            {
                Directory.CreateDirectory(RootDir);

                string dir = Path.Combine(RootDir, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
                int suffix = 1;
                while (Directory.Exists(dir))
                {
                    dir = Path.Combine(RootDir, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + suffix);
                    suffix++;
                }
                Directory.CreateDirectory(dir);
                string imgDir = Path.Combine(dir, ImageFolder);
                Directory.CreateDirectory(imgDir);

                int sectionSize = sections[0].Length;
                ArchiveMeta meta = new ArchiveMeta
                {
                    name = Path.GetFileName(dir),
                    createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    sectionSize = sectionSize,
                    sectionCount = sections.Length,
                    avgRadius = avgRadius
                };

                // 1) 截面环顶点 → 二进制（世界坐标，固定每截面点数，便于对齐）
                using (FileStream fs = new FileStream(Path.Combine(dir, ModelFile), FileMode.Create, FileAccess.Write))
                using (BinaryWriter bw = new BinaryWriter(fs))
                {
                    bw.Write(Magic);
                    bw.Write(Version);
                    bw.Write(sectionSize);
                    bw.Write(sections.Length);
                    bw.Write(avgRadius);

                    for (int s = 0; s < sections.Length; s++)
                    {
                        Vector3[] sec = sections[s];
                        int n = sec != null ? Mathf.Min(sectionSize, sec.Length) : 0;
                        for (int i = 0; i < n; i++)
                        {
                            bw.Write(sec[i].x);
                            bw.Write(sec[i].y);
                            bw.Write(sec[i].z);
                        }
                        for (int i = n; i < sectionSize; i++)
                        {
                            bw.Write(0f);
                            bw.Write(0f);
                            bw.Write(0f);
                        }
                    }
                }

                // 2) 缺陷帧图片 → PNG
                if (defects != null)
                {
                    for (int dI = 0; dI < defects.Count; dI++)
                    {
                        DefectSaveData d = defects[dI];
                        DefectEntry entry = new DefectEntry
                        {
                            angleDeg = d.angleDeg,
                            rangeDeg = d.rangeDeg,
                            markCount = d.markCount,
                            startSection = d.startSection,
                            typeName = d.typeName
                        };

                        for (int f = 0; f < d.frames.Count; f++)
                        {
                            Texture2D tex = d.frames[f];
                            if (tex == null)
                            {
                                continue;
                            }
                            try
                            {
                                byte[] png = tex.EncodeToPNG();
                                if (png == null)
                                {
                                    continue;
                                }
                                string file = $"d{dI}_{f:000}.png";
                                File.WriteAllBytes(Path.Combine(imgDir, file), png);
                                entry.images.Add(file);
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[PointCloudArchive] 图片写入失败：{ex.Message}");
                            }
                        }

                        meta.defects.Add(entry);
                    }
                }

                // 3) 元数据 → JSON
                File.WriteAllText(Path.Combine(dir, MetaFile), JsonUtility.ToJson(meta, true));

                Debug.Log($"[PointCloudArchive] 已保存存档：{dir}");
                return dir;
            }
            catch (Exception e)
            {
                Debug.LogError($"[PointCloudArchive] 保存失败：{e.Message}");
                return null;
            }
        }

        /// <summary>读取一个存档：截面环 + 缺陷定位参数 + 缺陷图片。</summary>
        public static ArchiveData Load(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    return null;
                }

                ArchiveMeta meta = ReadMeta(dir);
                if (meta == null)
                {
                    return null;
                }

                Vector3[][] sections = ReadSections(Path.Combine(dir, ModelFile));
                if (sections == null)
                {
                    return null;
                }

                ArchiveData data = new ArchiveData { meta = meta, sections = sections };
                string imgDir = Path.Combine(dir, ImageFolder);

                foreach (DefectEntry entry in meta.defects)
                {
                    DefectSaveData d = new DefectSaveData
                    {
                        angleDeg = entry.angleDeg,
                        rangeDeg = entry.rangeDeg,
                        markCount = entry.markCount,
                        startSection = entry.startSection,
                        typeName = entry.typeName
                    };

                    foreach (string file in entry.images)
                    {
                        string path = Path.Combine(imgDir, file);
                        if (!File.Exists(path))
                        {
                            continue;
                        }
                        Texture2D tex = new Texture2D(2, 2);
                        if (tex.LoadImage(File.ReadAllBytes(path)))
                        {
                            d.frames.Add(tex);
                        }
                        else
                        {
                            UnityEngine.Object.Destroy(tex);
                        }
                    }

                    data.defects.Add(d);
                }

                Debug.Log($"[PointCloudArchive] 已读取存档：{dir}（截面 {sections.Length} · 缺陷 {data.defects.Count}）");
                return data;
            }
            catch (Exception e)
            {
                Debug.LogError($"[PointCloudArchive] 读取失败：{e.Message}");
                return null;
            }
        }

        /// <summary>读取二进制截面环顶点。</summary>
        private static Vector3[][] ReadSections(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogError("[PointCloudArchive] 缺少 model.bin");
                return null;
            }

            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (BinaryReader br = new BinaryReader(fs))
            {
                if (br.ReadUInt32() != Magic)
                {
                    Debug.LogError("[PointCloudArchive] 存档格式不正确（magic 不匹配）");
                    return null;
                }

                br.ReadInt32(); // version
                int sectionSize = br.ReadInt32();
                int sectionCount = br.ReadInt32();
                br.ReadSingle(); // avgRadius（以 meta.json 为准）

                if (sectionSize <= 0 || sectionCount <= 0)
                {
                    return null;
                }

                Vector3[][] sections = new Vector3[sectionCount][];
                for (int s = 0; s < sectionCount; s++)
                {
                    Vector3[] sec = new Vector3[sectionSize];
                    for (int i = 0; i < sectionSize; i++)
                    {
                        sec[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    }
                    sections[s] = sec;
                }
                return sections;
            }
        }
    }
}
