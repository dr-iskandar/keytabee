//================================================================================================================================
//
//  Copyright (c) 2015-2023 VisionStar Information Technology (Shanghai) Co., Ltd. All Rights Reserved.
//  EasyAR is the registered trademark or trademark of VisionStar Information Technology (Shanghai) Co., Ltd in China
//  and other countries for the augmented reality technology developed by VisionStar Information Technology (Shanghai) Co., Ltd.
//
//================================================================================================================================

using UnityEngine;
using UnityEngine.Rendering;
#if EASYAR_URP_ENABLE
using UnityEngine.Rendering.Universal;
#elif EASYAR_LWRP_ENABLE
using UnityEngine.Rendering.LWRP;
#else
using ScriptableRendererFeature = UnityEngine.ScriptableObject;
#endif

namespace easyar
{
    /// <summary>
    /// <para xml:lang="en">A render feature for rendering the camera image for AR devies when URP in used. Add this render feature to the renderer feature list in forward renderer asset.</para>
    /// <para xml:lang="zh">使用URP时用来渲染AR设备相机图像的render feature。需要在forward renderer asset的renderer feature 列表中添加这个render feature。</para>
    /// </summary>
    public class EasyARCameraImageRendererFeature : ScriptableRendererFeature
    {
#if EASYAR_URP_ENABLE || EASYAR_LWRP_ENABLE
        CameraImageRenderPass renderPass;
        CameraImageRenderPass renderPassUser;
#if EASYAR_URP_13_1_OR_NEWER
        Optional<RTHandleSystem> rtHandleSystem;
#endif

        public override void Create()
        {
            renderPass = new CameraImageRenderPass();
            renderPassUser = new CameraImageRenderPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (!camera) { return; }

            var imageRenderer = CameraImageRenderer.TryGetRenderer(camera);
            if (!imageRenderer || !imageRenderer.Material) { return; }

            if (imageRenderer.enabled)
            {
                renderPass.Setup(imageRenderer.Material, imageRenderer.InvertCulling);
                renderer.EnqueuePass(renderPass);
            }
            if (imageRenderer.UserTexture.OnSome)
            {
#if EASYAR_URP_13_1_OR_NEWER
                if (rtHandleSystem.OnNone)
                {
                    rtHandleSystem = new RTHandleSystem();
                    rtHandleSystem.Value.Initialize(Screen.width, Screen.height);
                }
                renderPassUser.SetupRTHandleSystem(rtHandleSystem.Value);
#endif
                renderPassUser.Setup(imageRenderer.Material, imageRenderer.InvertCulling);
                renderPassUser.SetupTarget(imageRenderer.UserTexture.Value);
                renderer.EnqueuePass(renderPassUser);
            }
        }

#if EASYAR_URP_13_1_OR_NEWER
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                if (rtHandleSystem.OnSome)
                {
                    rtHandleSystem.Value.Dispose();
                    rtHandleSystem = null;
                }
            }
        }
#endif

        class CameraImageRenderPass : ScriptableRenderPass
        {
            static readonly Matrix4x4 projection = Matrix4x4.Ortho(0f, 1f, 0f, 1f, -0.1f, 9.9f);
            readonly Mesh mesh;
            Material material;
            bool invertCulling;
#if EASYAR_URP_13_1_OR_NEWER
            Optional<RTHandle> colorTarget;
            Optional<RTHandleSystem> rtHandleSystem;
#else
            Optional<RenderTargetIdentifier> colorTarget;
#endif

            public CameraImageRenderPass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
                mesh = new Mesh
                {
                    vertices = new Vector3[]
                    {
                        new Vector3(0f, 0f, 0.1f),
                        new Vector3(0f, 1f, 0.1f),
                        new Vector3(1f, 1f, 0.1f),
                        new Vector3(1f, 0f, 0.1f),
                    },
                    uv = new Vector2[]
                    {
                        new Vector2(0f, 0f),
                        new Vector2(0f, 1f),
                        new Vector2(1f, 1f),
                        new Vector2(1f, 0f),
                    },
                    triangles = new int[] { 0, 1, 2, 0, 2, 3 }
                };
            }

            public void Setup(Material mat, bool iCulling)
            {
                material = mat;
                invertCulling = iCulling;
            }

#if EASYAR_URP_13_1_OR_NEWER
            public void SetupRTHandleSystem(RTHandleSystem system)=> rtHandleSystem = system;
            public void SetupTarget(RenderTexture color) => colorTarget = rtHandleSystem.Value.Alloc(color);
#else
            public void SetupTarget(RenderTexture color) => colorTarget = (RenderTargetIdentifier)color;
#endif

            public override void Configure(CommandBuffer commandBuffer, RenderTextureDescriptor renderTextureDescriptor)
            {
                if (colorTarget.OnSome)
                {
                    ConfigureTarget(colorTarget.Value);
                }
                ConfigureClear(ClearFlag.Depth, Color.clear);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
#if EASYAR_URP_13_1_OR_NEWER
                if (rtHandleSystem.OnSome)
                {
                    rtHandleSystem.Value.SetReferenceSize(Screen.width, Screen.height);
                }
#endif
                var cmd = CommandBufferPool.Get();
                cmd.SetInvertCulling(invertCulling);
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, projection);
                cmd.DrawMesh(mesh, Matrix4x4.identity, material);
                cmd.SetViewProjectionMatrices(renderingData.cameraData.camera.worldToCameraMatrix, renderingData.cameraData.camera.projectionMatrix);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
#endif
    }
}
