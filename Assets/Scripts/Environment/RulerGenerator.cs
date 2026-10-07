using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// 在宿主物体（如 Cube）表面沿 Z 轴生成标尺刻度线与数字标注。
    /// 刻度线创建为独立世界空间对象，避免继承宿主物体的缩放。
    /// </summary>
    public class RulerGenerator : MonoBehaviour
    {
        [Header("标尺参数")]
        [SerializeField] private float spacing = 100f;
        [SerializeField] private float length = 1000f;
        [Tooltip("每隔几个刻度显示一个数字。")]
        [SerializeField] private int labelInterval = 10;

        [Header("刻度线外观")]
        [SerializeField] private float longTickHeight = 12f;
        [SerializeField] private float shortTickHeight = 6f;
        [SerializeField] private float tickDepth = 3f;
        [SerializeField] private float tickThickness = 0.5f;
        [SerializeField] private Color tickColor = Color.white;

        [Header("数字标注")]
        [SerializeField] private bool showLabels = true;
        [SerializeField] private float labelSize = 100f;
        [SerializeField] private Color labelColor = Color.white;

        private Font labelFont;

        private void Awake()
        {
            labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (labelFont == null)
            {
                labelFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            Generate();
        }

        private void Generate()
        {
            GameObject container = new GameObject("RulerMarks");
            container.transform.SetPositionAndRotation(transform.position, transform.rotation);

            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material material = new Material(shader);
            material.color = tickColor;

            float halfX = transform.lossyScale.x * 0.5f;
            int total = Mathf.FloorToInt(length / spacing);

            for (int i = 0; i <= total; i++)
            {
                float z = i * spacing;
                bool major = i % labelInterval == 0;

                CreateTick(container.transform, z, halfX, major, material);

                if (showLabels && major)
                {
                    CreateLabel(container.transform, z, halfX, z);
                }
            }
        }

        private void CreateTick(Transform parent, float z, float halfX, bool major, Material material)
        {
            GameObject tick = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tick.name = major ? "TickMajor" : "TickMinor";
            tick.transform.SetParent(parent, false);

            float height = major ? longTickHeight : shortTickHeight;
            tick.transform.localScale = new Vector3(tickDepth, height, tickThickness);
            tick.transform.localPosition = new Vector3(halfX + tickDepth * 0.5f, 0f, z);

            tick.GetComponent<MeshRenderer>().sharedMaterial = material;

            Collider col = tick.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }
        }

        private void CreateLabel(Transform parent, float z, float halfX, float value)
        {
            GameObject label = new GameObject("Label");
            label.transform.SetParent(parent, false);

            TextMesh tm = label.AddComponent<TextMesh>();
            tm.text = value.ToString("0");
            tm.font = labelFont;
            tm.characterSize = labelSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = labelColor;

            label.transform.localPosition = new Vector3(halfX + tickDepth + labelSize * 0.5f, 0f, z);
            label.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
        }
    }
}
