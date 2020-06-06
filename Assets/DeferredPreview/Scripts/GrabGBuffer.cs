using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;


namespace GbufferUtil {
    public class GrabGBuffer : MonoBehaviour
    {
        static GrabGBuffer Instance;

        [SerializeField]
        Shader gbufferCopyShader;
        Material gbufferCopyMaterial;

        Mesh quad;

        [SerializeField]
        RenderTexture depthTex;
        [SerializeField]
        RenderTexture[] gbufferTexes = new RenderTexture[4];

        static new Camera camera
        {
            get { return Camera.main; }
        }

        static public GrabGBuffer GetInstance()
        {
            return Instance;
        }

        static public RenderTexture GetDepthTexture()
        {
            return GetInstance().depthTex;
        }

        static public RenderTexture GetGBufferTexture(int index)
        {
            Assert.IsTrue(index >= 0 && index < 4);
            return GetInstance().gbufferTexes[index];
        }

        Mesh CreateQuad()
        {
            var mesh = new Mesh();
            mesh.name = "Quad";
            mesh.vertices = new Vector3[4] {
            new Vector3( 1f, 1f, 0f),
            new Vector3(-1f, 1f, 0f),
            new Vector3(-1f,-1f, 0f),
            new Vector3( 1f,-1f, 0f),
        };
            mesh.triangles = new int[6] {
            0, 1, 2,
            2, 3, 0
        };
            return mesh;
        }


        private void OnEnable()
        {
            Instance = this;
            UpdateRenderTextures();
        }

        void Start()
        {
            quad = CreateQuad();
            gbufferCopyMaterial = new Material(gbufferCopyShader);
        }

        private void OnDisable()
        {
            Instance = null;

            if(depthTex != null)
            {
                depthTex.Release();
                depthTex = null;
            }

            for(int i = 0; i < 4; i++)
            {
                if(gbufferTexes[i] != null)
                {
                    gbufferTexes[i].Release();
                    gbufferTexes[i] = null;
                }
            }
        }

        /*
        IEnumerator OnPostRender()
        {
            yield return new WaitForEndOfFrame();
            UpdateRenderTextures();
            UpdateGBuffer();
        }
        */

        public void OnPostRender()
        {
            UpdateRenderTextures();
            UpdateGBuffer();
        }

        RenderTexture CreateRenderTexture(RenderTextureFormat fmt, int depth)
        {
            var texture = new RenderTexture(camera.pixelWidth, camera.pixelHeight, depth, fmt);
            texture.filterMode = FilterMode.Point;
            texture.useMipMap = false;
            texture.autoGenerateMips = false;
            texture.enableRandomWrite = false;
            texture.Create();
            return texture;
        }

        void UpdateRenderTextures()
        {
            //update depthTex
            if(depthTex == null || depthTex.width != camera.pixelWidth 
                || depthTex.height != camera.pixelHeight)
            {
                if(depthTex != null) depthTex.Release();
                depthTex = CreateRenderTexture(RenderTextureFormat.Depth, 24);
            }

            //update gbufferTex
            for(int i = 0; i < 4; ++i)
            {
                if(gbufferTexes[i] == null ||
                    gbufferTexes[i].width != camera.pixelWidth ||
                    gbufferTexes[i].height != camera.pixelHeight)
                {
                    if(gbufferTexes[i] != null) gbufferTexes[i].Release();
                    gbufferTexes[i] = CreateRenderTexture(RenderTextureFormat.ARGB32, 0);
                }
            }
        }

        void UpdateGBuffer()
        {
            var gbufferes = new RenderBuffer[4];
            for(int i = 0; i < 4; i++)
            {
                gbufferes[i] = gbufferTexes[i].colorBuffer;
            }

            //Propertyのtextureに焼きこむ
            gbufferCopyMaterial.SetPass(0);
            Graphics.SetRenderTarget(gbufferes, depthTex.depthBuffer);
            Graphics.DrawMeshNow(quad, Matrix4x4.identity);
            Graphics.SetRenderTarget(null);
        } 
    }
}