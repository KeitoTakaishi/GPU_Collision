using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;



namespace GPUParticles
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

        //結合ソースとなるmesh, 結合する数
        Mesh CreateCombinedMesh(Mesh mesh, int num)
        {
            //triangleのindex配列
            var meshIndices = mesh.GetIndices(0);
            //indexの長さ
            var indexNum = meshIndices.Length;


            var vertices = new List<Vector3>();
            //mesh * indexの長さでindexの総数算出する
            var indices = new int[num * indexNum];
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uv0 = new List<Vector2>();
            var uv1 = new List<Vector2>();

            //結合したいmeshの数分だけループを回す
            for(int id = 0; id < num; ++id)
            {
                vertices.AddRange(mesh.vertices);
                normals.AddRange(mesh.normals);
                tangents.AddRange(mesh.tangents);
                uv0.AddRange(mesh.uv);

                // 各メッシュのインデックスは（1 つのモデルの頂点数 * ID）分ずらす
                for(int n = 0; n < indexNum; ++n)
                {
                    indices[id * indexNum + n] = id * mesh.vertexCount + meshIndices[n];
                }

                // 2 番目の UV に ID を格納しておく
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


        int GetParticlePoolSize()
        {
            particleArgsBuffer.SetData(particleArgs);
            ComputeBuffer.CopyCount(particlePoolBuffer, particleArgsBuffer, 0);
            particleArgsBuffer.GetData(particleArgs);
            return particleArgs[0];
        }


        void OnEnable()
        {
            Cursor.visible = false;
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


        private void OnDisable()
        {
            particleBuffer.Release();
            particlePoolBuffer.Release();
            particleArgsBuffer.Release();
        }


        //argsについていまいちよくわかっていない
        void initComputeBuffer()
        {
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

        void Start()
        {

        }

        void Update()
        {

            Cursor.visible = false;
            if(Input.GetMouseButton(0))
            {
                DispatchEmit();
            }
            DispatchEmit();
            DispatchUpdate();
            RegisterDraw(Camera.main);
        }

        

        void DispatchEmit()
        {
            computeShader.SetBuffer(emitKerenl, "_particles", particleBuffer);
            //ConsumeBuffer
            computeShader.SetBuffer(emitKerenl, "_particlePool", particlePoolBuffer);
            computeShader.SetVector("_angVelocity", angVelocity * Mathf.Deg2Rad);
            computeShader.SetVector("_range", range);
            computeShader.SetFloat("_scale", scale);
            computeShader.SetFloat("_deltaTime", Time.deltaTime);
            //computeShader.SetFloat("_ScreenWidth", )
            //computeShader.SetFloat("_ScreenHeight", )
            computeShader.SetFloat("_lifeTime", lifeTime);

            var particlePoolSize = GetParticlePoolSize();
            Debug.Log(particlePoolSize);
            if(particlePoolSize > 0)
                computeShader.Dispatch(emitKerenl, Mathf.Min(10, particlePoolSize / 8), 1, 1);
        }
    

        void DispatchUpdate()
        {
            //computeShader.SetFloats("_ViewProj", GetViewProjectionArray());
            //computeShader.SetTexture(updateKernel_, "_CameraDepthTexture", GBufferUtils.GetDepthTexture());
            //computeShader.SetTexture(updateKernel_, "_CameraGBufferTexture2", GBufferUtils.GetGBufferTexture(2));
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