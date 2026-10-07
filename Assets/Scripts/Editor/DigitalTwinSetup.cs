using UnityEditor;
using UnityEngine;

namespace DigitalTwin.Editor
{
    /// <summary>
    /// 编辑器一键搭建工具：在场景中创建数字孪生管理器。
    /// 运行时会自动装配小车、相机、遥测采集与 WebSocket 客户端。
    /// </summary>
    public static class DigitalTwinSetup
    {
        [MenuItem("Tools/Digital Twin/Setup Scene")]
        public static void SetupScene()
        {
            DigitalTwinManager existing = Object.FindObjectOfType<DigitalTwinManager>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[DigitalTwin] 管理器已存在，已选中。");
                return;
            }

            GameObject go = new GameObject("DigitalTwinManager");
            go.AddComponent<DigitalTwinManager>();
            Undo.RegisterCreatedObjectUndo(go, "Setup Digital Twin");
            Selection.activeGameObject = go;

            Debug.Log("[DigitalTwin] 管理器已创建。点击 Play 会自动装配小车与相机。");
        }
    }
}
