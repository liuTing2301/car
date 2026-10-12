using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DigitalTwin.Scripting
{
    /// <summary>一个本地保存的脚本（JSON 存储）。</summary>
    [Serializable]
    public class ScriptFile
    {
        public string name;
        public string createdAt;
        public string source;
    }

    /// <summary>
    /// 脚本本地存档：保存 / 列出 / 读取 / 删除。
    /// 目录：Application.persistentDataPath/PipeScripts/&lt;时间戳&gt;.json
    /// </summary>
    public static class ScriptStorage
    {
        private const string RootFolder = "PipeScripts";
        private const string Ext = ".json";

        public static string RootDir => Path.Combine(Application.persistentDataPath, RootFolder);

        /// <summary>列出所有脚本文件，按名称（时间戳）倒序，最新在前。</summary>
        public static List<string> List()
        {
            List<string> list = new List<string>();
            try
            {
                if (!Directory.Exists(RootDir)) return list;
                foreach (string f in Directory.GetFiles(RootDir, "*" + Ext))
                {
                    list.Add(f);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ScriptStorage] 列目录失败：{e.Message}");
            }
            list.Sort((a, b) => string.CompareOrdinal(Path.GetFileNameWithoutExtension(b), Path.GetFileNameWithoutExtension(a)));
            return list;
        }

        public static ScriptFile Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return JsonUtility.FromJson<ScriptFile>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogError($"[ScriptStorage] 读取失败：{e.Message}");
                return null;
            }
        }

        /// <summary>保存脚本，返回文件路径；失败返回 null。</summary>
        public static string Save(string name, string source)
        {
            try
            {
                Directory.CreateDirectory(RootDir);
                string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string path = Path.Combine(RootDir, stamp + Ext);
                int suffix = 1;
                while (File.Exists(path))
                {
                    path = Path.Combine(RootDir, stamp + "_" + suffix + Ext);
                    suffix++;
                }

                ScriptFile file = new ScriptFile
                {
                    name = string.IsNullOrEmpty(name) ? stamp : name,
                    createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    source = source ?? ""
                };
                File.WriteAllText(path, JsonUtility.ToJson(file, true));
                Debug.Log($"[ScriptStorage] 已保存脚本：{path}");
                return path;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ScriptStorage] 保存失败：{e.Message}");
                return null;
            }
        }

        public static bool Delete(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ScriptStorage] 删除失败：{e.Message}");
                return false;
            }
        }
    }
}
