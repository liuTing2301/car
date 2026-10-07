using System.Collections.Generic;
using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// 动态管道模型：沿小车走过的路径，用一个个圆环段圆滑拼接成半透明管道。
    /// 管道随小车移动逐渐变长（超过最大段数后尾部收缩，整体跟随小车），
    /// 每个采样点记录当时雷达检测到的半径，因此管道半径沿路径平滑变化。
    /// </summary>
    public class DynamicPipeModel : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private RobotDataReader robotDataReader;
        [SerializeField] private Transform car;

        [Header("采样参数")]
        [Tooltip("采样间隔（段长，越小越圆滑）。")]
        [SerializeField] private float sampleDistance = 1f;
        [Tooltip("最大段数（限制管道总长度）。")]
        [SerializeField] private int maxSegments = 3000;
        [Tooltip("圆周分段数，越高越圆滑。")]
        [SerializeField] private int radialSegments = 32;
        [Tooltip("半径数据 → 模型半径的换算系数。")]
        [SerializeField] private float radiusScale = 2000f;
        [Tooltip("管道颜色（半透明）。")]
        [SerializeField] private Color pipeColor = new Color(100f, 0f, 80f, 0.5f);

        [Header("半径过滤")]
        [Tooltip("置信度阈值，低于该值忽略当前半径（继承前一帧）。若置信度为 0~1 区间请改为 0.95。")]
        [SerializeField] private float confidenceThreshold = 95f;
        [Tooltip("半径最小变化量，变化小于该值视为噪声，继承前一帧半径。")]
        [SerializeField] private float minRadiusChange = 0.02f;

        private readonly List<Vector3> points = new List<Vector3>();
        private readonly List<float> radii = new List<float>();

        private MeshFilter meshFilter;
        private Mesh mesh;
        private Vector3 initialPos;
        private Vector3 lastSampleRel;
        private bool initialized;
        private float lastValidRadius = 1f;
        private bool hasLastValidRadius;

        public void Setup(RobotDataReader reader, Transform carTransform)
        {
            this.robotDataReader = reader;
            this.car = carTransform;
        }

        private void Awake()
        {
            meshFilter = gameObject.AddComponent<MeshFilter>();
            MeshRenderer mr = gameObject.AddComponent<MeshRenderer>();
            mr.material = CreateTransparentMaterial();

            mesh = new Mesh();
            mesh.MarkDynamic();
            meshFilter.mesh = mesh;
        }

        private void OnEnable()
        {
            // 每次重新显示时，从当前位置重新开始生长
            initialized = false;
            hasLastValidRadius = false;
            points.Clear();
            radii.Clear();
            if (mesh != null)
            {
                mesh.Clear();
            }
        }

        private void Update()
        {
            if (car == null)
            {
                return;
            }

            if (!initialized)
            {
                initialPos = car.position;
                transform.position = initialPos;
                transform.rotation = Quaternion.identity;
                initialized = true;
                lastSampleRel = Vector3.zero;
                AddSample(Vector3.zero);
                return;
            }

            Vector3 rel = car.position - initialPos;
            if (Vector3.Distance(rel, lastSampleRel) >= sampleDistance)
            {
                lastSampleRel = rel;
                AddSample(rel);
            }
        }

        private void AddSample(Vector3 relPos)
        {
            float radius = GetFilteredRadius();

            points.Add(relPos);
            radii.Add(radius);

            if (points.Count > maxSegments)
            {
                points.RemoveAt(0);
                radii.RemoveAt(0);
            }

            RebuildMesh();
        }

        /// <summary>
        /// 半径过滤：置信度低于阈值，或半径变化小于最小变化量时，
        /// 忽略当前帧数据，继承前一帧的有效半径。
        /// </summary>
        private float GetFilteredRadius()
        {
            if (robotDataReader == null || robotDataReader.LatestPipe == null)
            {
                return lastValidRadius;
            }

            PipeData p = robotDataReader.LatestPipe;
            float confidence = (float)p.confidence;
            float radius = (float)p.radius;

            // 第一帧
            if (!hasLastValidRadius)
            {
                hasLastValidRadius = true;
                lastValidRadius = confidence >= confidenceThreshold ? radius : 1f;
                return lastValidRadius;
            }

            // 置信度不足：忽略，继承前一帧
            if (confidence < confidenceThreshold)
            {
                return lastValidRadius;
            }

            // 半径变化过小：视为噪声，继承前一帧
            if (Mathf.Abs(radius - lastValidRadius) < minRadiusChange)
            {
                return lastValidRadius;
            }

            // 接受新半径
            lastValidRadius = radius;
            return lastValidRadius;
        }

        private void RebuildMesh()
        {
            int n = points.Count;
            if (n < 2)
            {
                mesh.Clear();
                return;
            }

            Vector3[] vertices = new Vector3[n * radialSegments];
            Vector2[] uv = new Vector2[n * radialSegments];

            for (int i = 0; i < n; i++)
            {
                // 路径切线（前后采样点差分）
                Vector3 tangent;
                if (i == 0)
                {
                    tangent = (points[1] - points[0]).normalized;
                }
                else if (i == n - 1)
                {
                    tangent = (points[n - 1] - points[n - 2]).normalized;
                }
                else
                {
                    tangent = (points[i + 1] - points[i - 1]).normalized;
                }

                // 构造垂直于切线的局部坐标系
                Vector3 up = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
                Vector3 right = Vector3.Cross(tangent, up).normalized;
                Vector3 up2 = Vector3.Cross(right, tangent).normalized;

                float r = Mathf.Max(0.05f, radii[i] * radiusScale);

                for (int j = 0; j < radialSegments; j++)
                {
                    float angle = (float)j / radialSegments * Mathf.PI * 2f;
                    Vector3 offset = (right * Mathf.Cos(angle) + up2 * Mathf.Sin(angle)) * r;
                    vertices[i * radialSegments + j] = points[i] + offset;
                    uv[i * radialSegments + j] = new Vector2((float)j / radialSegments, (float)i / (n - 1));
                }
            }

            int[] triangles = new int[(n - 1) * radialSegments * 6];
            int t = 0;
            for (int i = 0; i < n - 1; i++)
            {
                for (int j = 0; j < radialSegments; j++)
                {
                    int jNext = (j + 1) % radialSegments;
                    int a = i * radialSegments + j;
                    int b = i * radialSegments + jNext;
                    int c = (i + 1) * radialSegments + j;
                    int d = (i + 1) * radialSegments + jNext;

                    triangles[t++] = a;
                    triangles[t++] = c;
                    triangles[t++] = b;

                    triangles[t++] = b;
                    triangles[t++] = c;
                    triangles[t++] = d;
                }
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
        }

        private Material CreateTransparentMaterial()
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
            }

            Material mat = new Material(shader);
            mat.color = pipeColor;

            mat.SetFloat("_Mode", 3f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            return mat;
        }
    }
}
