﻿//
// ShadowBuffer.cs
//
// Projector For LWRP
//
// Copyright (c) 2020 NYAHOON GAMES PTE. LTD.
//

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.LWRP;

namespace ProjectorForLWRP
{
    [RequireComponent(typeof(ShadowMaterialProperties))]
    public class ShadowBuffer : MonoBehaviour, System.IComparable<ShadowBuffer>
    {
        public enum ApplyMethod
        {
            ByShadowProjectors,
            ByLitShaders,
            Both
        };

        public Material material;
        public string shadowTextureName = "_ShadowTex";
        [SerializeField]
        private StencilMaskBit m_stencilMask = 0;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        public PerObjectData perObjectData = PerObjectData.None;
        public ApplyMethod applyMethod = ApplyMethod.ByShadowProjectors;
        public LayerMask additionalIgnoreLayers = -1;
        public int stencilMask
        {
            get { return (int)m_stencilMask; }
            set { m_stencilMask = (StencilMaskBit)value; }
        }

        private Dictionary<Camera, List<ShadowProjectorForLWRP>> m_projectors = new Dictionary<Camera, List<ShadowProjectorForLWRP>>();
        private ApplyShadowBufferPass m_applyPass;
        private ShadowMaterialProperties m_shadowMaterialProperties;
        private int m_shadowTextureId;
        private void Initialize()
        {
            m_applyPass = new ApplyShadowBufferPass(this);
            m_shadowTextureId = Shader.PropertyToID(shadowTextureName);
            m_shadowMaterialProperties = GetComponent<ShadowMaterialProperties>();
        }
        ApplyShadowBufferPass applyPass
        {
            get
            {
                if (m_applyPass == null)
                {
                    Initialize();
                }
                return m_applyPass;
            }
        }
        ShadowMaterialProperties shadowMaterialProperties
        {
            get
            {
                if (m_shadowMaterialProperties == null)
                {
                    Initialize();
                }
                return m_shadowMaterialProperties;
            }
        }
#if UNITY_EDITOR
        private void OnValidate()
        {
            if (material == null)
            {
                material = HelperFunctions.FindMaterial("Projector For LWRP/ShadowBuffer/Apply Shadow Buffer");
            }
        }
#endif
        internal int GetSortIndex(ref RenderingData renderingData)
        {
            int additionalLightIndex;
            int lightIndex = shadowMaterialProperties.FindLightSourceIndex(ref renderingData, out additionalLightIndex);
            if (0 <= additionalLightIndex)
            {
                return additionalLightIndex;
            }
            else if (lightIndex == renderingData.lightData.mainLightIndex)
            {
                return renderingData.lightData.additionalLightsCount;
            }
            else if (0 < lightIndex)
            {
                return renderingData.lightData.additionalLightsCount + 1 + lightIndex;
            }
            else
            {
                return renderingData.lightData.additionalLightsCount + 1 + renderingData.lightData.visibleLights.Length;
            }
        }

        // use IComparable for sorting shadow buffer list because lambda expression cannot use 'ref'.
        // This can avoid making a copy of RenderingData.
        private int m_sortIndex;
        internal void CalculateSortIndex(ref RenderingData renderingData)
        {
            m_sortIndex = GetSortIndex(ref renderingData);
        }
        public int CompareTo(ShadowBuffer rhs)
        {
            return m_sortIndex - rhs.m_sortIndex;
        }
        
		internal void RegisterProjector(Camera cam, ShadowProjectorForLWRP projector)
        {
            if (material == null)
            {
                return;
            }
            List<ShadowProjectorForLWRP> projectors;
            if (!m_projectors.TryGetValue(cam, out projectors))
            {
                projectors = new List<ShadowProjectorForLWRP>();
                m_projectors.Add(cam, projectors);
            }
            projectors.Add(projector);
        }
        internal void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData, out int applyPassCount)
        {
            List<ShadowProjectorForLWRP> projectors;
            applyPassCount = 0;
            if (applyMethod == ApplyMethod.ByLitShaders)
            {
                return;
            }
            if (m_projectors.TryGetValue(renderingData.cameraData.camera, out projectors))
            {
                if (0 < projectors.Count)
                {
                    applyPass.renderPassEvent = renderPassEvent;
                    renderer.EnqueuePass(applyPass);
                    ++applyPassCount;
                }
            }
        }
        private bool m_appliedToLightPass = false;
        private CollectShadowBufferPass.RenderTextureRef m_shadowTextureRef = null;
        private int m_shadowTextureColorChannelIndex = 0;
        internal void CollectShadowBuffer(ScriptableRenderContext context, ref RenderingData renderingData, CollectShadowBufferPass.RenderTextureRef textureRef, int channelIndex)
        {
            m_shadowTextureRef = textureRef;
            m_shadowTextureColorChannelIndex = channelIndex;
            textureRef.Retain((ColorWriteMask)colorWriteMask);
            List <ShadowProjectorForLWRP> projectors;
            if (m_projectors.TryGetValue(renderingData.cameraData.camera, out projectors))
            {
                if (projectors != null)
                {
                    for (int i = 0; i < projectors.Count; ++i)
                    {
                        projectors[i].CollectShadows(context, ref renderingData);
                    }
                    m_appliedToLightPass = false;
                    if (applyMethod != ApplyMethod.ByShadowProjectors) {
                        int additionalLightIndex;
                        int lightIndex = shadowMaterialProperties.FindLightSourceIndex(ref renderingData, out additionalLightIndex);
                        if (0 <= lightIndex)
                        {
                            if (lightIndex == renderingData.lightData.mainLightIndex)
                            {
                                LitShaderState.SetMainLightShadow(m_shadowTextureRef.renderTexture, m_shadowTextureColorChannelIndex);
                                m_appliedToLightPass = true;
                            }
                            else if (0 <= additionalLightIndex)
                            {
                                m_appliedToLightPass = LitShaderState.SetAdditionalLightShadow(additionalLightIndex, m_shadowTextureRef.renderTexture, m_shadowTextureColorChannelIndex);
                                m_appliedToLightPass = true;
                            }
                        }
                    }
                }
            }
        }
        static readonly string[] KEYWORD_SHADOWTEX_CHANNELS = { "P4LWRP_SHADOWTEX_CHANNEL_A", "P4LWRP_SHADOWTEX_CHANNEL_B", "P4LWRP_SHADOWTEX_CHANNEL_G", "P4LWRP_SHADOWTEX_CHANNEL_R" };
        internal void ApplyShadowBuffer(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            PerObjectData requiredPerObjectData;

            bool appliedToLightPass = m_appliedToLightPass;
            m_appliedToLightPass = false;
            if (appliedToLightPass && additionalIgnoreLayers == -1)
            {
                return;
            }
            if (stencilMask == 0) {
#if UNITY_EDITOR
                // stencilMask can be zero only if applyMethod == ApplyMethod.ByLitShader.
                Debug.LogWarning("Apply Shadow Buffer Pass was skipped because stencilMask was 0.", this);
#endif
                return;
            }
            if (!shadowMaterialProperties.UpdateMaterialProperties(material, ref renderingData, out requiredPerObjectData))
            {
                return;
            }
            for (int i = 0; i < KEYWORD_SHADOWTEX_CHANNELS.Length; ++i)
            {
                if (m_shadowTextureColorChannelIndex == i)
                {
                    material.EnableKeyword(KEYWORD_SHADOWTEX_CHANNELS[i]);
                }
                else
                {
                    material.DisableKeyword(KEYWORD_SHADOWTEX_CHANNELS[i]);
                }
            }
            requiredPerObjectData |= perObjectData;
            List<ShadowProjectorForLWRP> projectors;
            if (m_projectors.TryGetValue(renderingData.cameraData.camera, out projectors))
            {
                if (projectors != null)
                {
                    material.SetTexture(m_shadowTextureId, GetTemporaryShadowTexture());
                    for (int i = 0; i < projectors.Count; ++i)
                    {
                        projectors[i].ApplyShadowBuffer(context, ref renderingData, requiredPerObjectData, appliedToLightPass ? (int)additionalIgnoreLayers : 0);
                    }
                }
            }
        }
        internal void ClearProjectosForCamera(Camera camera)
        {
            List<ShadowProjectorForLWRP> projectors;
            if (m_projectors.TryGetValue(camera, out projectors))
            {
                if (projectors != null)
                {
                    projectors.Clear();
                }
            }
        }
        internal int colorWriteMask
        {
            get { return 1 << m_shadowTextureColorChannelIndex; }
        }
        internal Texture GetTemporaryShadowTexture()
        {
            if (m_shadowTextureRef == null)
            {
                return null;
            }
            return m_shadowTextureRef.renderTexture;
        }
        internal void ReleaseTemporaryShadowTexture()
        {
            if (m_shadowTextureRef != null)
            {
                m_shadowTextureRef.Release((ColorWriteMask)colorWriteMask);
            }
        }
    }
}