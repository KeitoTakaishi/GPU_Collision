using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GPUCollision
{

    struct Particle
    {
        public bool active;
        public Vector3 position;
        public Vector3 velocity;
        public Vector3 rotation;
        public Vector3 angVelocity;
        public Color color;
        public float scale;
        public float time;
        public float lifeTime;
    }


    public class GPUParticleManager : MonoBehaviour
    {
        const int MAX_VERTEX_COUNT = 65534;
        [SerializeField]
        int maxParticleNum;
        [SerializeField]
        Mesh srcMesh;
        [SerializeField]
        Shader shader;
        [SerializeField]
        ComputeShader computeShader;

        #region GPUParticleProperties
        [SerializeField]
        float timeSpeed = 5.0f;
        [SerializeField]
        Vector3 velocity = new Vector3(2.0f, 5.0f, 2.0f);
        [SerializeField]
        Vector3 angVelocity = new Vector3(45f, 45f, 45f);
        [SerializeField]
        Vector3 range = Vector3.one;
        [SerializeField]
        float scale = 0.2f;
        [SerializeField]
        float lifeTime = 2f;
        [SerializeField, Range(1, 100)]
        int emitGroupNum = 10;
        #endregion

        #region InstancingParames
        Mesh combinedMesh;
        ComputeBuffer particleBuffer;
        ComputeBuffer particlePoolBuffer;
        ComputeBuffer particleArgsBuffer;
        int[] particleArgs;
        int updateKernel;
        int emitKerenl;
        Material material;
        List<MaterialPropertyBlock> propertyBlocks = new List<MaterialPropertyBlock>();
        int particleNumPerMesh;
        int meshNum;
        #endregion



        void OnEnable()
        {
            //Cursor.visible = false;
            {
                particleNumPerMesh = MAX_VERTEX_COUNT / srcMesh.vertexCount;
                meshNum = (int)Mathf.Ceil((float)maxParticleNum / particleNumPerMesh);
                combinedMesh = CreateCombinedMesh(srcMesh, particleNumPerMesh);
                Debug.Log("meshNum : " + meshNum);
            }


            material = new Material(shader);
            for(int i = 0; i < meshNum; i++)
            {
                var props = new MaterialPropertyBlock();
                props.SetFloat("_IdOffset", particleNumPerMesh * i);
                propertyBlocks.Add(props);
            }


            //ComputeShader
            initComputeBuffer();
            updateKernel = computeShader.FindKernel("Update");
            emitKerenl = computeShader.FindKernel("Emit");
            DispatchInit();
        }

        void Start()
        {

        }

        void Update()
        {
            //if(Input.GetMouseButton(0))
            //{
            //    DispatchEmit();
            //}

            if(Input.GetMouseButton(0))
            {
                RaycastHit hit;
                var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                if(Physics.Raycast(ray, out hit))
                {
                    var toNormal = Quaternion.FromToRotation(Vector3.up, hit.normal);
                    computeShader.SetVector("_position", hit.point + hit.normal * 3.0f);
                    //computeShader.SetVector("_velocity", toNormal * velocity);
                    DispatchEmit();
                }
            }
            //DispatchEmit();
            DispatchUpdate();
            RegisterDraw(Camera.main);
        }

        private void OnDisable()
        {
            particleBuffer.Release();
            particlePoolBuffer.Release();
            particleArgsBuffer.Release();
        }


        Mesh CreateCombinedMesh(Mesh mesh, int num)
        {
            var meshIndices = mesh.GetIndices(0);
            var indexNum = meshIndices.Length;
            var vertices = new List<Vector3>();
            var indices = new int[num * indexNum];
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uv0 = new List<Vector2>();
            var uv1 = new List<Vector2>();

            for(int id = 0; id < num; ++id)
            {
                vertices.AddRange(mesh.vertices);
                normals.AddRange(mesh.normals);
                tangents.AddRange(mesh.tangents);
                uv0.AddRange(mesh.uv);

                for(int n = 0; n < indexNum; ++n)
                {
                    indices[id * indexNum + n] = id * mesh.vertexCount + meshIndices[n];
                }

                for(int n = 0; n < mesh.uv.Length; ++n)
                {
                    uv1.Add(new Vector2(id, id));
                }
            }

            var combinedMesh = new Mesh();
            combinedMesh.SetVertices(vertices);
            combinedMesh.SetIndices(indices, MeshTopology.Triangles, 0);
            combinedMesh.SetNormals(normals);
            combinedMesh.RecalculateNormals();
            combinedMesh.SetTangents(tangents);
            combinedMesh.SetUVs(0, uv0);
            combinedMesh.SetUVs(1, uv1);
            combinedMesh.RecalculateBounds();
            combinedMesh.bounds.SetMinMax(Vector3.one * -100f, Vector3.one * 100f);

            return combinedMesh;
        }

        float[] GetViewProjectionArray()
        {
            var camera = Camera.main;
            var view = camera.worldToCameraMatrix;
            var proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            var vp = proj * view;
            return new float[] {
            vp.m00, vp.m10, vp.m20, vp.m30,
            vp.m01, vp.m11, vp.m21, vp.m31,
            vp.m02, vp.m12, vp.m22, vp.m32,
            vp.m03, vp.m13, vp.m23, vp.m33
            };
        }

        int GetParticlePoolSize()
        {
            particleArgsBuffer.SetData(particleArgs);
            ComputeBuffer.CopyCount(particlePoolBuffer, particleArgsBuffer, 0);
            particleArgsBuffer.GetData(particleArgs);
            return particleArgs[0];
        }


        //Init ComputeBuffers(particleBufer, appendBuffer, argsBuffer) 
        void initComputeBuffer()
        {
            Debug.Log("initComputeBuffer");
            Debug.Log("maxParticleNum : " + maxParticleNum);
            particleBuffer = new ComputeBuffer(maxParticleNum, Marshal.SizeOf(typeof(Particle)), ComputeBufferType.Default);

            //AppendStructuredBuffer, ComsumeStructuredBuffer
            particlePoolBuffer = new ComputeBuffer(maxParticleNum, sizeof(int), ComputeBufferType.Append);
            particlePoolBuffer.SetCounterValue(0);


            //?? 引数に注意
            particleArgsBuffer = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);
            particleArgs = new int[] { 0, 1, 0, 0 };
        }

        void DispatchInit()
        {
            var initKernel = computeShader.FindKernel("Init");
            computeShader.SetBuffer(initKernel, "_particles", particleBuffer);
            computeShader.SetBuffer(initKernel, "_deadList", particlePoolBuffer);
            computeShader.Dispatch(initKernel, maxParticleNum / 8, 1, 1);
        }



        void DispatchEmit()
        {

            computeShader.SetBuffer(emitKerenl, "_particles", particleBuffer);
            computeShader.SetBuffer(emitKerenl, "_particlePool", particlePoolBuffer);
            computeShader.SetVector("_velocity", velocity);
            computeShader.SetVector("_angVelocity", angVelocity * Mathf.Deg2Rad);
            computeShader.SetVector("_range", range);
            computeShader.SetFloat("_scale", scale);
            computeShader.SetFloat("_deltaTime", Time.deltaTime);
            computeShader.SetFloat("_screenWidth", Camera.main.pixelWidth);
            computeShader.SetFloat("_screenHeight", Camera.main.pixelHeight);
            computeShader.SetFloat("_lifeTime", lifeTime);

            var particlePoolSize = GetParticlePoolSize();
            if(particlePoolSize > 0)
            {
                //Debug.Log("ParticlePoolSize : " + particlePoolSize);
                computeShader.Dispatch(emitKerenl, Mathf.Min(10, particlePoolSize / 8), 1, 1);
            }
        }

        void DispatchUpdate()
        {
            computeShader.SetFloats("_ViewProj", GetViewProjectionArray());

            var cam = Camera.main;
            computeShader.SetMatrix("_view", cam.worldToCameraMatrix);
            computeShader.SetMatrix("_proj", GL.GetGPUProjectionMatrix(cam.projectionMatrix, false));
            //computeShader.SetMatrix("_proj", GL.GetGPUProjectionMatrix(cam.projectionMatrix, true));


            computeShader.SetTexture(updateKernel, "_CameraDepthTex", GBufferUtils.GetDepthTexture());
            computeShader.SetTexture(updateKernel, "_CameraGBufferTex2", GBufferUtils.GetGBufferTexture(2));
            computeShader.SetFloat("_screenWidth",  cam.pixelWidth);
            computeShader.SetFloat("_screenHeight", cam.pixelHeight);
            computeShader.SetFloat("_timeSpeed", timeSpeed);
            //TODO : normal
            computeShader.SetBuffer(updateKernel, "_particles", particleBuffer);
            computeShader.SetBuffer(updateKernel, "_deadList", particlePoolBuffer);
            computeShader.Dispatch(updateKernel, maxParticleNum / 8, 1, 1);
        }

        void RegisterDraw(Camera camera)
        {
            material.SetBuffer("_Particles", particleBuffer);
            for(int i = 0; i < meshNum; ++i)
            {
                var prop = propertyBlocks[i];
                prop.Clear();
                prop.SetFloat("_IdOffset", particleNumPerMesh * i);
                Graphics.DrawMesh(combinedMesh, transform.position, transform.rotation, material, 0, camera, 0, prop);
            }
        }
    }
}