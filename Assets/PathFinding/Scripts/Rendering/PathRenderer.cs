﻿using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using UnityEngine;
using UnityEngine.Rendering;

namespace PathFinding
{
    
    public class PathRenderer : MonoBehaviour {

        [SerializeField] protected Material material;
        [SerializeField] protected ComputeShader compute;
        [SerializeField] protected int segmentsCount = 20;
        [SerializeField] protected float thickness = 0.25f;
        [SerializeField] protected float speed = 1f;

        [SerializeField] protected Graph graph;

        [SerializeField] protected BlendMode srcBlend = BlendMode.One, dstBlend = BlendMode.One;

        protected Mesh mesh;
        protected int pathCount = 0, instancesCount = 0;
        protected ComputeBuffer pathBuffer, segmentBuffer, vertexBuffer;
        protected ComputeBuffer argsBuffer;
        uint[] args = new uint[5] { 0, 0, 0, 0, 0 };

        #region MonoBehaviour functions

        protected void Start () {
        }
        
        protected void Update () {
            if (instancesCount <= 0) return;

            Sequence(Time.deltaTime * speed);
            ViewAlign();
            Check();

            material.SetBuffer("_Vertices", vertexBuffer);
            material.SetInt("_VertexCount", segmentsCount * 2);
            material.SetMatrix("_WorldToLocal", transform.worldToLocalMatrix);
            material.SetMatrix("_LocalToWorld", transform.localToWorldMatrix);
            material.SetInt("_SrcBlend", (int)srcBlend);
            material.SetInt("_DstBlend", (int)dstBlend);
            Graphics.DrawMeshInstancedIndirect(mesh, 0, material, new Bounds(Vector3.zero, Vector3.one * 1000f), argsBuffer, 0, null);
        }

        protected void OnDestroy()
        {
            if(pathBuffer != null)
            {
                pathBuffer.Release();
                segmentBuffer.Release();
                vertexBuffer.Release();
                argsBuffer.Release();
            }
        }

        #endregion

        public void Setup(PathBundle bundle)
        {
            pathCount = bundle.Count;
            instancesCount = bundle.pathes.Count;

            pathBuffer = new ComputeBuffer(instancesCount * bundle.Count, Marshal.SizeOf(typeof(Vector3)));
            pathBuffer.SetData(bundle.GetData());

            segmentBuffer = new ComputeBuffer(instancesCount * segmentsCount, Marshal.SizeOf(typeof(Segment_t)));
            segmentBuffer.SetData(new Segment_t[segmentBuffer.count]);

            vertexBuffer = new ComputeBuffer(segmentBuffer.count * 2, Marshal.SizeOf(typeof(Vector3)));
            vertexBuffer.SetData(new Vector3[vertexBuffer.count]);

            mesh = Build(segmentsCount);
            args[0] = mesh.GetIndexCount(0);
            args[1] = (uint)instancesCount;
            argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            argsBuffer.SetData(args);

            compute.SetFloat("_Longest", bundle.Longest);

            Initialize();
        }

        protected void Initialize ()
        {
            var kernel = compute.FindKernel("Initialize");
            compute.SetBuffer(kernel, "_Segments", segmentBuffer);
            compute.SetInt("_PathCount", pathCount);
            compute.SetInt("_SegmentsCount", segmentsCount);
            compute.SetInt("_InstancesCount", instancesCount);

            uint tx, ty, tz;
            compute.GetKernelThreadGroupSizes(kernel, out tx, out ty, out tz);

            compute.Dispatch(kernel, segmentsCount / (int)tx + 1, instancesCount / (int)ty + 1, (int)tz);
        }

        protected void Sequence (float dt)
        {
            var kernel = compute.FindKernel("Sequence");
            compute.SetBuffer(kernel, "_Path", pathBuffer);
            compute.SetBuffer(kernel, "_Segments", segmentBuffer);
            compute.SetInt("_PathCount", pathCount);
            compute.SetInt("_SegmentsCount", segmentsCount);
            compute.SetInt("_InstancesCount", instancesCount);
            compute.SetFloat("_DT", dt);

            uint tx, ty, tz;
            compute.GetKernelThreadGroupSizes(kernel, out tx, out ty, out tz);

            compute.Dispatch(kernel, segmentsCount / (int)tx + 1, instancesCount / (int)ty + 1, (int)tz);
        }

        protected void Check ()
        {
            var kernel = compute.FindKernel("Check");
            compute.SetBuffer(kernel, "_Path", pathBuffer);
            compute.SetBuffer(kernel, "_Segments", segmentBuffer);
            compute.SetInt("_PathCount", pathCount);
            compute.SetInt("_SegmentsCount", segmentsCount);
            compute.SetInt("_InstancesCount", instancesCount);

            uint tx, ty, tz;
            compute.GetKernelThreadGroupSizes(kernel, out tx, out ty, out tz);

            compute.Dispatch(kernel, instancesCount / (int)tx + 1, (int)ty, (int)tz);
        }

        protected void ViewAlign()
        {
            var kernel = compute.FindKernel("ViewAlign");
            compute.SetBuffer(kernel, "_Segments", segmentBuffer);
            compute.SetBuffer(kernel, "_Vertices", vertexBuffer);
            compute.SetInt("_SegmentsCount", segmentsCount);
            compute.SetInt("_InstancesCount", instancesCount);
            compute.SetFloat("_Thickness", thickness);

            var localCamDir = transform.InverseTransformDirection(Camera.main.transform.forward);
            compute.SetVector("_LocalCameraDirection", localCamDir);

            uint tx, ty, tz;
            compute.GetKernelThreadGroupSizes(kernel, out tx, out ty, out tz);

            compute.Dispatch(kernel, segmentsCount / (int)tx + 1, instancesCount / (int)ty + 1, (int)tz);
        }

        #region static functions

        public static Mesh Build(int count)
        {
            var mesh = new Mesh();

            var vertices = new Vector3[count * 2];
            var uv = new Vector2[count * 2];
            var indices = new List<int>();

            float inv = 1f / (count - 1);
            for(int i = 0; i < count; i++)
            {
                var idx = i * 2;

                vertices[idx] = new Vector3(-1, i, 0);
                vertices[idx + 1] = new Vector3(1, i, 0);

                uv[idx] = new Vector2(0f, inv * i);
                uv[idx + 1] = new Vector2(1f, inv * i);

                if(i < count - 1)
                {
                    indices.Add(idx); indices.Add(idx + 1); indices.Add(idx + 2);
                    indices.Add(idx + 3); indices.Add(idx + 2); indices.Add(idx + 1);
                }
            }

            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.SetIndices(indices.ToArray(), MeshTopology.Triangles, 0);

            return mesh;
        }

        #endregion

    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Segment_t
    {
        public Vector3 position;
        public float t;
        public float speed;
        public float ratio;
    }

}

