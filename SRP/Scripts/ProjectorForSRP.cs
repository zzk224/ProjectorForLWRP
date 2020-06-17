﻿//
// ProjectorForSRP.cs
//
// Projector For SRP
//
// Copyright (c) 2020 NYAHOON GAMES PTE. LTD.
//

using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace ProjectorForSRP
{
	/// <summary>
	/// Abstract class of Projector for Scriptable Render Pipeline
	///
	/// This class implements Awake, OnDestroy, OnEnable, OnDisable functions.
	/// In the derived class please do not implement Awake or OnDestroy.
	/// Instead, please override Initialize or Cleanup.
	/// If the derived class needs OnEnable or OnDisable,
	/// then please override the function and call base.OnEnable or base.OnDisable.
	/// 
	/// </summary>
	[ExecuteInEditMode]
	[RequireComponent(typeof(Projector))]
	public abstract class ProjectorForSRP : MonoBehaviour
    {
		// get Unity Projector component
		public Projector projector { get; private set; }

		public Matrix4x4 localToProjectorTexcoordMatrix
		{
			get { return m_projectionMatrix; }
		}
		public Matrix4x4 worldToProjectorTexcoordMatrix
		{
			get
			{
				return m_projectionMatrix * transform.worldToLocalMatrix;
			}
		}
		public Vector3 localProjectorDirection
		{
			get { return Vector3.forward; }
		}
		public Vector3 worldProjectorDirection
		{
			get
			{
				return transform.forward;
			}
		}

		// set this to false if your scriptable render pipeline does not need culling results for rendering projectors.
		// for example, deferred rendering can render projectors without culling results.
		protected bool m_requiresCullingResult = true;

		// try to get the culling results. only valid within a frame after AddProjectorToRenderer called.
		protected bool TryGetCullingResults(Camera camera, out CullingResults cullingResults)
		{
			Debug.Assert(m_requiresCullingResult);
			return m_cullingResults.TryGetValue(camera, out cullingResults);
		}

		private Material m_copiedProjectorMaterial = null;
		protected Material GetDuplicatedProjectorMaterial()
		{
			if (m_copiedProjectorMaterial == null)
			{
				m_copiedProjectorMaterial = new Material(projector.material);
			}
			else if (m_copiedProjectorMaterial.shader != projector.material.shader)
			{
				m_copiedProjectorMaterial.shader = projector.material.shader;
			}
			m_copiedProjectorMaterial.CopyPropertiesFromMaterial(projector.material);
			return m_copiedProjectorMaterial;
		}

		protected void EnableProjectorForLWRPKeyword(Material material)
		{
			material.EnableKeyword(PROJECTOR_SHADER_KEYWORD);
		}

		protected void SetupProjectorMatrix(Material material)
		{
			material.SetMatrix(s_shaderPropIdFsrWorldToProjector, worldToProjectorTexcoordMatrix);
			material.SetVector(s_shaderPropIdFsrWorldProjectDir, worldProjectorDirection);
		}

		protected void GetDefaultDrawSettings(Camera camera, Material material, ShaderTagId[] shaderTagIds, out DrawingSettings drawingSettings, out FilteringSettings filteringSettings)
		{
			drawingSettings = new DrawingSettings(shaderTagIds[0], new SortingSettings(camera));
			for (int i = 1; i < shaderTagIds.Length; ++i)
			{
				drawingSettings.SetShaderPassName(i, shaderTagIds[i]);
			}
			drawingSettings.overrideMaterial = material;
			drawingSettings.overrideMaterialPassIndex = 0;
			drawingSettings.enableDynamicBatching = true; // default value is true. please change it before draw call if needed.
			drawingSettings.enableInstancing = material.enableInstancing;
			drawingSettings.perObjectData = PerObjectData.None; // default value is None. please change it before draw call if needed.

			// default render queue range is opaque. please change it before draw call if needed.
			filteringSettings = new FilteringSettings(RenderQueueRange.opaque, ~projector.ignoreLayers);
		}

		//
		// functions to be overridden
		//

		/// <summary>
		/// This function is called from Awake().
		/// Please do not implement Awake function in the derived class.
		/// Instead, please override Initialize function.
		/// 
		/// base.Initialize() must be called in the overriding function.
		/// Please be careful of the timing. Because OnProjectorFrustumChanged will be called from base.Initialize().
		/// If OnProjectorFrustumChanged requires some setups, they must be done before base.Initialize().
		/// 
		/// <seealso cref="Cleanup"/>
		/// <seealso cref="OnProjectorFrustumChanged"/>
		/// </summary>
		protected virtual void Initialize()
		{
			projector = GetComponent<Projector>();
			if (projector == null)
			{
				projector = gameObject.AddComponent<Projector>();
			}
			UpdateFrustum();
			m_projectorFrustumHash = CalculateProjectorFrustumHash(projector);
		}

		/// <summary>
		/// This function is called from OnDestroy().
		/// Please do not implemen OnDestroy function in the derived class.
		/// Instead, please override Cleanup function.
		/// <seealso cref="Initialize"/>
		/// </summary>
		protected virtual void Cleanup()
		{
		}

		/// <summary>
		/// Register this projector to the render pipeline specific renderer.
		/// This function is called every frame if the projector frustum is visible from the camera.
		/// </summary>
		protected abstract void AddProjectorToRenderer(Camera camera);

		/// <summary>
		/// This function is called when projector frustum changed at the begining of frame rendering.
		/// Also this function is called from Initialize. Please finish necessary setups for this function befor base.Initialize called.
		///
		/// If you need a mesh of projector frustum for stencil test or deferred rendering,
		/// please use SetProjectorFrustumVerticesToMesh(mesh) function inside the ovrriding function.
		///
		/// <seealso cref="Initialize"/>
		/// <seealso cref="SetProjectorFrustumVerticesToMesh"/>
		/// </summary>
		protected virtual void OnProjectorFrustumChanged()
		{
		}

		protected virtual void Awake()
		{
			StaticInitialize();
			Initialize();
		}

		protected virtual void OnDestroy()
		{
			Cleanup();
		}

		protected virtual void OnEnable()
		{
			RenderPipelineManager.beginFrameRendering += OnBeginFrameRendering;
		}

		protected virtual void OnDisable()
		{
			RenderPipelineManager.beginFrameRendering -= OnBeginFrameRendering;
		}


		//
		// Helper functions
		//

		/// <summary>
		/// Set positions of the projector frustom vertices and trinangle indices to mesh.
		/// Use this funtion in OnProjectorFrustumChanged() if necessary.
		/// </summary>
		protected void SetProjectorFrustumVerticesToMesh(Mesh mesh)
		{
			mesh.vertices = m_frustumVertices;
			mesh.triangles = s_frustumTriangles;
		}

		//
		// private functions
		//
		private Vector3[] m_frustumVertices;
		private Matrix4x4 m_projectionMatrix;

		private static bool s_isInitialized = false;
		private static int s_shaderPropIdFsrWorldToProjector = -1;
		private static int s_shaderPropIdFsrWorldProjectDir = -1;
		static readonly int[] s_frustumTriangles = {
			0, 1, 2, 2, 1, 3, // near plane
 			0, 4, 1, 1, 4, 5, // left
 			1, 5, 3, 3, 5, 7, // top
			3, 7, 2, 2, 7, 6, // right
			2, 6, 0, 0, 6, 4, // bottom
			6, 7, 4, 4, 7, 5  // far plane
		};

		private const string PROJECTOR_SHADER_KEYWORD = "FSR_PROJECTOR_FOR_LWRP";

		static protected void StaticInitialize()
		{
			if (!s_isInitialized)
			{
				s_shaderPropIdFsrWorldToProjector = Shader.PropertyToID("_FSRWorldToProjector");
				s_shaderPropIdFsrWorldProjectDir = Shader.PropertyToID("_FSRWorldProjectDir");
				s_isInitialized = true;
			}
		}

		static ProjectorForSRP()
		{
			StaticInitialize();
		}

		private ulong m_projectorFrustumHash = 0;
		private Dictionary<Camera, CullingResults> m_cullingResults;
		private void OnBeginFrameRendering(ScriptableRenderContext context, Camera[] cameras)
		{
			if (!isActiveAndEnabled)
			{
				return;
			}
			if (!projector.enabled)
			{
				return;
			}
			if (projector.material == null)
			{
				return;
			}
			if (m_cullingResults == null)
			{
				m_cullingResults = new Dictionary<Camera, CullingResults>();
			}
			m_cullingResults.Clear();
			ulong hash = CalculateProjectorFrustumHash(projector);
			if (hash != m_projectorFrustumHash)
			{
				UpdateFrustum();
				m_projectorFrustumHash = hash;
			}
			for (int i = 0, count = cameras.Length; i < count; ++i)
			{
				Camera cam = cameras[i];
				if ((cam.cullingMask & (1 << gameObject.layer)) != 0)
				{
					if (StartCullingIfVisible(context, cam))
					{
						AddProjectorToRenderer(cam);
					}
				}
			}
		}
		static ulong CalculateProjectorFrustumHash(Projector projector)
		{
			ulong hash = (ulong)projector.nearClipPlane.GetHashCode();
			hash = (hash << 16) | (hash >> 48);
			if (projector.orthographic)
			{
				hash = (hash << 1) | (hash >> 63);
				hash ^= (ulong)projector.orthographicSize.GetHashCode();
			}
			else
			{
				hash ^= 0x1;
				hash = (hash << 1) | (hash >> 63);
				hash ^= (ulong)projector.fieldOfView.GetHashCode();
			}
			hash = (hash << 16) | (hash >> 48);
			hash ^= (ulong)projector.farClipPlane.GetHashCode();
			hash = (hash << 16) | (hash >> 48);
			hash ^= (ulong)projector.farClipPlane.GetHashCode();
			return hash;
		}

		private void UpdateFrustum()
		{
			float w, h;
			if (m_frustumVertices == null)
			{
				m_frustumVertices = new Vector3[8];
			}
			if (projector.orthographic)
			{
				h = projector.orthographicSize;
				w = h * projector.aspectRatio;
				m_frustumVertices[0].x = m_frustumVertices[1].x = m_frustumVertices[4].x = m_frustumVertices[5].x = -w;
				m_frustumVertices[2].x = m_frustumVertices[3].x = m_frustumVertices[6].x = m_frustumVertices[7].x = w;
				m_frustumVertices[0].y = m_frustumVertices[2].y = m_frustumVertices[4].y = m_frustumVertices[6].y = -h;
				m_frustumVertices[1].y = m_frustumVertices[3].y = m_frustumVertices[5].y = m_frustumVertices[7].y = h;
			}
			else
			{
				float tan = Mathf.Tan(0.5f * Mathf.Deg2Rad * projector.fieldOfView);
				h = tan * projector.farClipPlane;
				w = h * projector.aspectRatio;
				float nearH = tan * projector.nearClipPlane;
				float nearW = nearH * projector.aspectRatio;
				m_frustumVertices[0].x = m_frustumVertices[1].x = -nearW;
				m_frustumVertices[4].x = m_frustumVertices[5].x = -w;
				m_frustumVertices[2].x = m_frustumVertices[3].x = nearW;
				m_frustumVertices[6].x = m_frustumVertices[7].x = w;
				m_frustumVertices[0].y = m_frustumVertices[2].y = -nearH;
				m_frustumVertices[4].y = m_frustumVertices[6].y = -h;
				m_frustumVertices[1].y = m_frustumVertices[3].y = nearH;
				m_frustumVertices[5].y = m_frustumVertices[7].y = h;
			}
			m_frustumVertices[0].z = m_frustumVertices[1].z = m_frustumVertices[2].z = m_frustumVertices[3].z = projector.nearClipPlane;
			m_frustumVertices[4].z = m_frustumVertices[5].z = m_frustumVertices[6].z = m_frustumVertices[7].z = projector.farClipPlane;
			UpdateProjectionMatrix();
			OnProjectorFrustumChanged();
		}
		private class TemporaryData
		{
			public TemporaryData()
			{
				m_vertices = new Vector3[8];
				m_clipPlanes = new Plane[12];
			}
			public Vector3[] m_vertices;
			public Plane[] m_clipPlanes;
		};
		private TemporaryData m_temporaryData = null;
		private bool StartCullingIfVisible(ScriptableRenderContext context, Camera cam)
		{
			if (m_frustumVertices == null)
			{
				return false;
			}
			ScriptableCullingParameters cullingParameters = new ScriptableCullingParameters();
			if (!cam.TryGetCullingParameters(IsStereoEnabled(cam), out cullingParameters))
			{
				return false;
			}
			if (m_temporaryData == null)
			{
				m_temporaryData = new TemporaryData();
			}
			uint flags = 0xff;
			ulong flags64 = 0;
			for (int i = 0; i < 8; ++i)
			{
				Vector3 v = m_temporaryData.m_vertices[i] = transform.TransformPoint(m_frustumVertices[i]);
				uint f = 0;
				for (int j = 0; j < cullingParameters.cullingPlaneCount; ++j)
				{
					Plane plane = cullingParameters.GetCullingPlane(j);
					if (plane.GetDistanceToPoint(v) < 0)
					{
						f |= (1U << j);
					}
				}
				flags &= f;
				flags64 |= (((ulong)f) << (8 * i));
			}
			if (flags != 0)
			{
				// projector is not visible from the camera
				return false;
			}
			if (!m_requiresCullingResult)
			{
				return true;
			}
			uint cameraPlanes = 0;
			int planeCount = 0;
			// -x
			flags = (uint)((flags64 >> 0) & (flags64 >> 8) & (flags64 >> 32) & (flags64 >> 40)) & 0xFF;
			if (flags == 0)
			{
				m_temporaryData.m_clipPlanes[planeCount++] = new Plane(m_temporaryData.m_vertices[0], m_temporaryData.m_vertices[1], m_temporaryData.m_vertices[4]);
			}
			else
			{
				cameraPlanes |= flags;
			}
			// +x
			flags = (uint)((flags64 >> 16) & (flags64 >> 24) & (flags64 >> 48) & (flags64 >> 56)) & 0xFF;
			if (flags == 0)
			{
				m_temporaryData.m_clipPlanes[planeCount++] = new Plane(m_temporaryData.m_vertices[3], m_temporaryData.m_vertices[2], m_temporaryData.m_vertices[7]);
			}
			else
			{
				cameraPlanes |= flags;
			}
			// -y
			flags = (uint)((flags64 >> 0) & (flags64 >> 16) & (flags64 >> 32) & (flags64 >> 48)) & 0xFF;
			if (flags == 0)
			{
				m_temporaryData.m_clipPlanes[planeCount++] = new Plane(m_temporaryData.m_vertices[2], m_temporaryData.m_vertices[0], m_temporaryData.m_vertices[6]);
			}
			else
			{
				cameraPlanes |= flags;
			}
			// +y
			flags = (uint)((flags64 >> 8) & (flags64 >> 24) & (flags64 >> 40) & (flags64 >> 56)) & 0xFF;
			if (flags == 0)
			{
				m_temporaryData.m_clipPlanes[planeCount++] = new Plane(m_temporaryData.m_vertices[1], m_temporaryData.m_vertices[3], m_temporaryData.m_vertices[5]);
			}
			else
			{
				cameraPlanes |= flags;
			}
			// near
			flags = (uint)((flags64 >> 0) & (flags64 >> 8) & (flags64 >> 16) & (flags64 >> 24)) & 0xFF;
			if (flags == 0)
			{
				m_temporaryData.m_clipPlanes[planeCount++] = new Plane(m_temporaryData.m_vertices[0], m_temporaryData.m_vertices[2], m_temporaryData.m_vertices[1]);
			}
			else
			{
				cameraPlanes |= flags;
			}
			// far
			flags = (uint)((flags64 >> 32) & (flags64 >> 40) & (flags64 >> 48) & (flags64 >> 56)) & 0xFF;
			if (flags == 0)
			{
				m_temporaryData.m_clipPlanes[planeCount++] = new Plane(m_temporaryData.m_vertices[4], m_temporaryData.m_vertices[5], m_temporaryData.m_vertices[6]);
			}
			else
			{
				cameraPlanes |= flags;
			}
			int maxPlaneCount = ScriptableCullingParameters.maximumCullingPlaneCount;
			for (int i = 0; i < cullingParameters.cullingPlaneCount && planeCount < maxPlaneCount; ++i)
			{
				if ((cameraPlanes & (1U << i)) != 0)
				{
					m_temporaryData.m_clipPlanes[planeCount++] = cullingParameters.GetCullingPlane(i);
				}
			}
			cullingParameters.cullingPlaneCount = planeCount;
			for (int i = 0; i < planeCount; ++i)
			{
				cullingParameters.SetCullingPlane(i, m_temporaryData.m_clipPlanes[i]);
			}
#if DEBUG
			// To avoid the error: Assertion failed on expression: 'params.cullingPlaneCount == kPlaneFrustumNum'
			cullingParameters.cullingPlaneCount = 6;
#endif
			cullingParameters.cullingOptions &= ~(CullingOptions.NeedsReflectionProbes | CullingOptions.ShadowCasters);
			CullingResults cullingResults = context.Cull(ref cullingParameters);
			m_cullingResults.Add(cam, cullingResults);
			return true;
		}

		static bool IsStereoEnabled(Camera camera)
		{
			bool isGameCamera = (camera.cameraType == CameraType.Game || camera.cameraType == CameraType.VR);
			bool isCompatWithXRDimension = true;
#if ENABLE_VR && ENABLE_VR_MODULE
            isCompatWithXRDimension &= (camera.targetTexture ? camera.targetTexture.dimension == UnityEngine.XR.XRSettings.deviceEyeTextureDimension : true);
#endif
			return XRGraphics.enabled && isGameCamera && (camera.stereoTargetEye == StereoTargetEyeMask.Both) && isCompatWithXRDimension;
		}

		private void UpdateProjectionMatrix()
		{
			Matrix4x4 matProjection;
			if (projector.orthographic)
			{
				float x = projector.aspectRatio * projector.orthographicSize;
				float y = projector.orthographicSize;
				matProjection = Matrix4x4.Ortho(x, -x, y, -y, projector.nearClipPlane, projector.farClipPlane);
			}
			else
			{
				matProjection = Matrix4x4.Perspective(projector.fieldOfView, projector.aspectRatio, projector.nearClipPlane, projector.farClipPlane);
			}
			matProjection.m00 *= -0.5f;
			matProjection.m02 += 0.5f * matProjection.m32;
			matProjection.m03 += 0.5f * matProjection.m33;
			matProjection.m11 *= -0.5f;
			matProjection.m12 += 0.5f * matProjection.m32;
			matProjection.m13 += 0.5f * matProjection.m33;
			float zScale = 1.0f / (projector.farClipPlane - projector.nearClipPlane);
			matProjection.m22 = zScale;
			matProjection.m23 = -zScale * projector.nearClipPlane;
			m_projectionMatrix = matProjection;
		}
	}
}
