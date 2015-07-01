﻿using EVEManager;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ShaderLoader;
using UnityEngine;
using Utils;

namespace Atmosphere
{
    public class Clouds2DMaterial : MaterialManager
    {
        [Persistent] 
        Color _Color = new Color(1,1,1,1);
        [Persistent]
        String _MainTex = "";
        [Persistent]
        String _DetailTex = "";
        [Persistent]
        float _FalloffPow = 2f;
        [Persistent]
        float _FalloffScale = 3f;
        [Persistent]
        float _DetailScale = 100f;
        [Persistent, InverseScaled]
        float _DetailDist = 0.000002f;
        [Persistent]
        float _MinLight = .5f;
        [Persistent, Scaled]
        float _FadeDist = 8f;
        [Persistent, InverseScaled]
        float _FadeScale = 0.00375f;
        [Persistent, InverseScaled]
        float _RimDist = 0.0001f;
        [Persistent, InverseScaled]
        float _RimDistSub = 1.01f;
        [Persistent, InverseScaled]
        float _InvFade = .008f;
    }

    class Clouds2D
    {
        GameObject CloudMesh;
        Material CloudMaterial;
        Projector ShadowProjector = null;
        GameObject ShadowProjectorGO = null;

        [Persistent]
        bool shadow = true;
        [Persistent]
        Vector3 shadowOffset = new Vector3(0, 0, 0);
        [Persistent]
        Clouds2DMaterial macroCloudMaterial;

        bool isScaled = false;
        public bool Scaled
        {
            get { return CloudMesh.layer == EVEManagerClass.SCALED_LAYER; }
            set
            {
                AtmosphereManager.Log("Clouds2D is now " + (value ? "SCALED" : "MACRO"));
                if (isScaled != value)
                {
                    if (value)
                    {
                        macroCloudMaterial.ApplyMaterialProperties(CloudMaterial, ScaledSpace.ScaleFactor);
                        macroCloudMaterial.ApplyMaterialProperties(ShadowProjector.material, ScaledSpace.ScaleFactor);
                        float scale = (float)(1000f / celestialBody.Radius);
                        CloudMaterial.DisableKeyword("SOFT_DEPTH_ON");
                        Reassign(EVEManagerClass.SCALED_LAYER, scaledCelestialTransform, scale);
                    }
                    else
                    {
                        macroCloudMaterial.ApplyMaterialProperties(CloudMaterial);
                        macroCloudMaterial.ApplyMaterialProperties(ShadowProjector.material);
                        CloudMaterial.EnableKeyword("SOFT_DEPTH_ON");
                        Reassign(EVEManagerClass.MACRO_LAYER, celestialBody.transform, 1);
                    }
                    isScaled = value;
                }
            }
        }
        CelestialBody celestialBody = null;
        Transform scaledCelestialTransform = null;
        Transform sunTransform;
        float radius;     
        float radiusScale;
        
        private static Shader cloudShader = null;
        private static Shader CloudShader
        {
            get
            {
                if (cloudShader == null)
                {
                    cloudShader = ShaderLoaderClass.FindShader("EVE/Cloud");
                } return cloudShader;
            }
        }

        private static Shader cloudShadowShader = null;
        private static Shader CloudShadowShader
        {
            get
            {
                if (cloudShadowShader == null)
                {
                    cloudShadowShader = ShaderLoaderClass.FindShader("EVE/CloudShadow");
                } return cloudShadowShader;
            }
        }

        internal void Apply(CelestialBody celestialBody, Transform scaledCelestialTransform, float radius, float speed)
        {
            Remove();
            this.celestialBody = celestialBody;
            this.scaledCelestialTransform = scaledCelestialTransform;
            HalfSphere hp = new HalfSphere(radius, ref CloudMaterial, CloudShader);
            CloudMesh = hp.GameObject;
            this.radius = radius;

            if (shadow)
            {
                ShadowProjectorGO = new GameObject();
                ShadowProjector = ShadowProjectorGO.AddComponent<Projector>();
                ShadowProjector.nearClipPlane = 10;
                ShadowProjector.fieldOfView = 60;
                ShadowProjector.aspectRatio = 1;
                ShadowProjector.orthographic = true;
                ShadowProjector.transform.parent = celestialBody.transform;
                ShadowProjector.material = new Material(CloudShadowShader);
            }
            sunTransform = Sun.Instance.sun.transform;

            Scaled = true;
        }

        public void Reassign(int layer, Transform parent, float scale)
        {
            CloudMesh.transform.parent = parent;
            CloudMesh.transform.localPosition = Vector3.zero;
            CloudMesh.transform.localScale = scale * Vector3.one;
            CloudMesh.layer = layer;

            radiusScale = radius * scale;
            float worldRadiusScale = Vector3.Distance(parent.transform.TransformPoint(Vector3.up * radiusScale), parent.transform.TransformPoint(Vector3.zero));
            
            if (ShadowProjector != null)
            {

                float dist = (float)(2 * worldRadiusScale);
                ShadowProjector.farClipPlane = dist;
                ShadowProjector.orthographicSize = worldRadiusScale;

                ShadowProjector.transform.parent = parent;
                //ShadowProjector.transform.localScale = scale * Vector3.one;
                ShadowProjectorGO.layer = layer;
                if (layer == EVEManagerClass.MACRO_LAYER)
                {
                    ShadowProjector.ignoreLayers = ~((1 << 19) | (1 << 15) | 2 | 1);
                    sunTransform = Tools.GetCelestialBody(Sun.Instance.sun.bodyName).transform;
                }
                else
                {
                    ShadowProjectorGO.layer = EVEManagerClass.SCALED_LAYER;
                    ShadowProjector.ignoreLayers = ~((1 << 29) | (1 << 23) | (1 << 18) | (1 << 10));// | (1 << 9));
                    sunTransform = Tools.GetScaledTransform(Sun.Instance.sun.bodyName);
                    AtmosphereManager.Log("Camera mask: "+ScaledCamera.Instance.camera.cullingMask);
                }
            }
        }

        public void Remove()
        {
            if (CloudMesh != null)
            {
                CloudMesh.transform.parent = null;
                GameObject.DestroyImmediate(CloudMesh);
                CloudMesh = null;
            }
            if(ShadowProjector != null)
            {
                ShadowProjector.transform.parent = null;
                GameObject.DestroyImmediate(ShadowProjector);
                ShadowProjector = null;
            }
        }

        internal void UpdateRotation(Quaternion rotation, Matrix4x4 World2Planet, double geoRotation, double texRoation, double shadowRotation, Vector2 offset)
        {
            if (rotation != null)
            {
                SetMeshRotation(rotation, World2Planet);
                if (ShadowProjector != null)
                {
                    Vector3 worldSunDir = Vector3.Normalize(Sun.Instance.sunDirection);
                    Vector3 sunDirection = Vector3.Normalize(ShadowProjector.transform.parent.InverseTransformDirection(worldSunDir));//sunTransform.position));
                    ShadowProjector.transform.localPosition = radiusScale * -sunDirection;
                    ShadowProjector.transform.forward = worldSunDir;
                }
            }
            
            SetTextureOffset(texRoation, shadowRotation, offset);
        }

        private void SetMeshRotation(Quaternion rotation, Matrix4x4 World2Planet)
        {
            CloudMesh.transform.localRotation = rotation;
            CloudMaterial.SetMatrix(EVEManagerClass.WORLD_2_PLANET_PROPERTY, (World2Planet*CloudMesh.transform.localToWorldMatrix));
        }

        private void SetTextureOffset(double texRotation, double shadowRotation, Vector2 offset)
        {
            
            Vector2 texOffset = new Vector2((float)texRotation + offset.x, offset.y);
            CloudMaterial.SetVector(EVEManagerClass.MAINOFFSET_PROPERTY, texOffset);

            if (ShadowProjector != null)
            {
                
                Vector4 texVect = ShadowProjector.transform.localPosition.normalized;

                texVect.w = ((float)shadowRotation + offset.x + .25f);
                ShadowProjector.material.SetVector(EVEManagerClass.MAINOFFSET_PROPERTY, texVect);
            }
        }

    }
}
