using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Solver : MonoBehaviour
{
    const int numHashes = 1<<20;
    const int numThreads = 1<<10; // Compute shader dependent value.
    public int numParticles = 1024;
    public float initSize = 10;
    public float radius = 1;
    public float gasConstant = 2000;
    public float restDensity = 10;
    public float mass = 1;
    public float density = 1;
    public float viscosity = 0.01f;
    public float gravity = 9.8f;
    public float deltaTime = 0.001f;

    public Vector3 minBounds = new Vector3(-10, -10, -10);
    public Vector3 maxBounds = new Vector3(10, 10, 10);

    public ComputeShader solverShader;

    public Shader renderShader;
    private Material renderMat;

    public Mesh particleMesh;
    public float particleRenderSize = 0.5f;

    public Color primaryColor;
    public Color secondaryColor;

    private ComputeBuffer hashesBuffer;
    private ComputeBuffer globalHashCounterBuffer;
    private ComputeBuffer localIndicesBuffer;
    private ComputeBuffer inverseIndicesBuffer;
    private ComputeBuffer particlesBuffer;
    private ComputeBuffer sortedBuffer;
    private ComputeBuffer forcesBuffer;
    private ComputeBuffer groupArrBuffer;
    private ComputeBuffer hashDebugBuffer;

    private ComputeBuffer drawInstancedArgsBuffer;

    private int solverFrame = 0;

    private int moveParticleBeginIndex = 0;
    public int moveParticles = 10;

    private double lastFrameTimestamp = 0;
    private double totalFrameTime = 0;

    // @Temp: Just for fun.
    private int boundsState = 0;
    private float waveTime = 0;
    private Vector4[] boxPlanes = new Vector4[7];
    private Vector4[] wavePlanes = new Vector4[7];
    private Vector4[] groundPlanes = new Vector4[7];

    struct Particle {
        public Vector4 pos; // with pressure.
        public Vector4 vel;
    }

    Vector4 GetPlaneEq(Vector3 p, Vector3 n) {
        return new Vector4(n.x, n.y, n.z, -Vector3.Dot(p, n));
    }

    void UpdateParams() {
        if (Input.GetKeyDown(KeyCode.X)) {
            boundsState++;
        }

        Vector4[] currPlanes;
        switch (boundsState) {
            case 0: currPlanes = boxPlanes;
            break;

            case 1: currPlanes = wavePlanes;
            break;

            default: currPlanes = groundPlanes;
            break;
        }

        if (currPlanes == wavePlanes) {
            waveTime += Time.deltaTime;
        }

        boxPlanes[0] = GetPlaneEq(new Vector3(0, 0, 0), Vector3.up);
        boxPlanes[1] = GetPlaneEq(new Vector3(0, 100, 0), Vector3.down);
        boxPlanes[2] = GetPlaneEq(new Vector3(-50, 0, 0), Vector3.right);
        boxPlanes[3] = GetPlaneEq(new Vector3(50, 0, 0), Vector3.left);
        boxPlanes[4] = GetPlaneEq(new Vector3(0, 0, -50), Vector3.forward);
        boxPlanes[5] = GetPlaneEq(new Vector3(0, 0, 50), Vector3.back);

        wavePlanes[0] = GetPlaneEq(new Vector3(0, 0, 0), Vector3.up);
        wavePlanes[1] = GetPlaneEq(new Vector3(0, 100, 0), Vector3.down);
        wavePlanes[2] = GetPlaneEq(new Vector3(-50 + Mathf.Pow(Mathf.Sin(Mathf.Min(waveTime*0.25f, Mathf.PI*0.5f)),2) * 32f, 0, 0), Vector3.right);
        wavePlanes[3] = GetPlaneEq(new Vector3(50 - Mathf.Pow(Mathf.Sin(Mathf.Min(waveTime*0.25f, Mathf.PI*0.5f)),2) * 32f, 0, 0), Vector3.left);
        wavePlanes[4] = GetPlaneEq(new Vector3(0, 0, -50 + Mathf.Pow(Mathf.Sin(Mathf.Min(waveTime*0.25f, Mathf.PI*0.5f)),2) * 32f), Vector3.forward);
        wavePlanes[5] = GetPlaneEq(new Vector3(0, 0, 50 - Mathf.Pow(Mathf.Sin(Mathf.Min(waveTime*0.25f, Mathf.PI*0.5f)),2) * 32f), Vector3.back);

        groundPlanes[0] = GetPlaneEq(new Vector3(0, 0, 0), Vector3.up);
        groundPlanes[1] = GetPlaneEq(new Vector3(0, 100, 0), Vector3.down);

        solverShader.SetVectorArray("planes", currPlanes);
    }

    void Start() {
        Particle[] particles = new Particle[numParticles];

        // Two dam break situation.
        Vector3 origin1 = new Vector3(
            Mathf.Lerp(minBounds.x, maxBounds.x, 0.25f),
            minBounds.y + initSize * 0.5f,
            Mathf.Lerp(minBounds.z, maxBounds.z, 0.25f)
        );
        Vector3 origin2 = new Vector3(
            Mathf.Lerp(minBounds.x, maxBounds.x, 0.75f),
            minBounds.y + initSize * 0.5f,
            Mathf.Lerp(minBounds.z, maxBounds.z, 0.75f)
        );

        for (int i = 0; i < numParticles; i++) {
            Vector3 pos = new Vector3(
                Random.Range(0f, 1f) * initSize - initSize * 0.5f,
                Random.Range(0f, 1f) * initSize - initSize * 0.5f,
                Random.Range(0f, 1f) * initSize - initSize * 0.5f
            );

            pos += (i % 2 == 0) ? origin1 : origin2;

            particles[i].pos = pos;
        }

        solverShader.SetInt("numHash", numHashes);
        solverShader.SetInt("numParticles", numParticles);

        solverShader.SetFloat("radiusSqr", radius * radius);
        solverShader.SetFloat("radius", radius);
        solverShader.SetFloat("gasConst", gasConstant);
        solverShader.SetFloat("restDensity", restDensity);
        solverShader.SetFloat("mass", mass);
        solverShader.SetFloat("viscosity", viscosity);
        solverShader.SetFloat("gravity", gravity);
        solverShader.SetFloat("deltaTime", deltaTime);

        float poly6 = 315f / (64f * Mathf.PI * Mathf.Pow(radius, 9f));
        float spiky = 45f / (Mathf.PI * Mathf.Pow(radius, 6f));
        float visco = 45f / (Mathf.PI * Mathf.Pow(radius, 6f));

        solverShader.SetFloat("poly6Coeff", poly6);
        solverShader.SetFloat("spikyCoeff", spiky);
        solverShader.SetFloat("viscoCoeff", visco);

        UpdateParams();

        hashesBuffer = new ComputeBuffer(numParticles, 4);
        // @Todo: Do we really need these?
        hashesBuffer.SetData(new uint[numParticles]);

        globalHashCounterBuffer = new ComputeBuffer(numHashes, 4);
        globalHashCounterBuffer.SetData(new uint[numHashes]);

        localIndicesBuffer = new ComputeBuffer(numParticles, 4);
        localIndicesBuffer.SetData(new uint[numParticles]);

        inverseIndicesBuffer = new ComputeBuffer(numParticles, 4);
        inverseIndicesBuffer.SetData(new uint[numParticles]);

        particlesBuffer = new ComputeBuffer(numParticles, 4 * 8);
        particlesBuffer.SetData(particles);

        sortedBuffer = new ComputeBuffer(numParticles, 4 * 8);
        sortedBuffer.SetData(new Particle[numParticles]);

        forcesBuffer = new ComputeBuffer(numParticles * 2, 4 * 4);
        forcesBuffer.SetData(new Vector4[numParticles]);

        int groupArrLen = Mathf.CeilToInt(numHashes / 1024f);
        groupArrBuffer = new ComputeBuffer(groupArrLen, 4);
        groupArrBuffer.SetData(new uint[groupArrLen]);

        hashDebugBuffer = new ComputeBuffer(3, 4);
        hashDebugBuffer.SetData(new uint[3]);

        for (int i = 0; i < 11; i++) {
            solverShader.SetBuffer(i, "hashes", hashesBuffer);
            solverShader.SetBuffer(i, "globalHashCounter", globalHashCounterBuffer);
            solverShader.SetBuffer(i, "localIndices", localIndicesBuffer);
            solverShader.SetBuffer(i, "inverseIndices", inverseIndicesBuffer);
            solverShader.SetBuffer(i, "particles", particlesBuffer);
            solverShader.SetBuffer(i, "sorted", sortedBuffer);
            solverShader.SetBuffer(i, "forces", forcesBuffer);
            solverShader.SetBuffer(i, "groupArr", groupArrBuffer);
            solverShader.SetBuffer(i, "hashDebug", hashDebugBuffer);
        }

        renderMat = new Material(renderShader);
        renderMat.SetBuffer("particles", particlesBuffer);
        renderMat.SetFloat("radius", particleRenderSize * 0.5f);

        drawInstancedArgsBuffer = new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);

        uint[] args = new uint[5];
        args[0] = particleMesh.GetIndexCount(0);
        args[1] = (uint) numParticles;
        args[2] = particleMesh.GetIndexStart(0);
        args[3] = particleMesh.GetBaseVertex(0);
        args[4] = 0;

        drawInstancedArgsBuffer.SetData(args);
    }

    void Update() {
        // Update solver.
        {
            UpdateParams();

            if (Input.GetMouseButton(0)) {
                Ray mouseRay = Camera.main.ScreenPointToRay(Input.mousePosition);
                RaycastHit hit;
                if (Physics.Raycast(mouseRay, out hit)) {
                    Vector3 pos = new Vector3(
                        Mathf.Clamp(hit.point.x, minBounds.x, maxBounds.x),
                        maxBounds.y - 1f,
                        Mathf.Clamp(hit.point.z, minBounds.z, maxBounds.z)
                    );

                    solverShader.SetInt("moveBeginIndex", moveParticleBeginIndex);
                    solverShader.SetInt("moveSize", moveParticles);
                    solverShader.SetVector("movePos", pos);
                    solverShader.SetVector("moveVel", Vector3.down * 70);

                    solverShader.Dispatch(solverShader.FindKernel("MoveParticles"), 1, 1, 1);

                    moveParticleBeginIndex = (moveParticleBeginIndex + moveParticles * moveParticles) % numParticles;
                }
            }

            renderMat.SetColor("primaryColor", primaryColor.linear);
            renderMat.SetColor("secondaryColor", secondaryColor.linear);

            double solverStart = Time.realtimeSinceStartupAsDouble;

            solverShader.Dispatch(solverShader.FindKernel("ResetCounter"), Mathf.CeilToInt((float)numHashes / numThreads), 1, 1);
            solverShader.Dispatch(solverShader.FindKernel("InsertToBucket"), Mathf.CeilToInt((float)numParticles / numThreads), 1, 1);

            // Debug
            if (Input.GetKeyDown(KeyCode.C)) {
                uint[] debugResult = new uint[3];

                hashDebugBuffer.SetData(debugResult);

                solverShader.Dispatch(solverShader.FindKernel("DebugHash"), Mathf.CeilToInt((float)numHashes / numThreads), 1, 1);

                hashDebugBuffer.GetData(debugResult);

                uint usedHashBuckets = debugResult[0];
                uint maxSameHash = debugResult[1];

                Debug.Log($"Total buckets: {numHashes}, Used buckets: {usedHashBuckets}, Used rate: {(float)usedHashBuckets / numHashes * 100}%");
                Debug.Log($"Avg hash collision: {(float)numParticles / usedHashBuckets}, Max hash collision: {maxSameHash}");
            }

            solverShader.Dispatch(solverShader.FindKernel("PrefixSum1"), Mathf.CeilToInt((float)numHashes / numThreads), 1, 1);

            // @Important: Because of the way prefix sum algorithm implemented,
            // Currently maximum numHashes value is numThreads^2.
            Debug.Assert(numHashes <= numThreads*numThreads);
            solverShader.Dispatch(solverShader.FindKernel("PrefixSum2"), 1, 1, 1);

            solverShader.Dispatch(solverShader.FindKernel("PrefixSum3"), Mathf.CeilToInt((float)numHashes / numThreads), 1, 1);
            solverShader.Dispatch(solverShader.FindKernel("Sort"), Mathf.CeilToInt((float)numParticles / numThreads), 1, 1);

            for (int iter = 0; iter < 1; iter++) {
                solverShader.Dispatch(solverShader.FindKernel("CalcPressure"), Mathf.CeilToInt((float)numParticles / numThreads), 1, 1);
                solverShader.Dispatch(solverShader.FindKernel("CalcForces"), Mathf.CeilToInt((float)numParticles / numThreads), 1, 1);
                solverShader.Dispatch(solverShader.FindKernel("Step"), Mathf.CeilToInt((float)numParticles / numThreads), 1, 1);
            }

            if (solverFrame > 1) {
                totalFrameTime += Time.realtimeSinceStartupAsDouble - lastFrameTimestamp;
            }
            lastFrameTimestamp = Time.realtimeSinceStartupAsDouble;

            if (solverFrame == 400 || solverFrame == 1200) {
                Debug.Log($"Avg frame time at #{solverFrame}: {totalFrameTime / (solverFrame-1) * 1000}ms.");
            }
        }

        Graphics.DrawMeshInstancedIndirect(
            particleMesh,
            0,
            renderMat,
            new Bounds(Vector3.zero, Vector3.one * 1000),
            drawInstancedArgsBuffer
        );
    }

    void OnDisable() {
        hashesBuffer.Dispose();
        globalHashCounterBuffer.Dispose();
        localIndicesBuffer.Dispose();
        inverseIndicesBuffer.Dispose();
        particlesBuffer.Dispose();
        sortedBuffer.Dispose();
        forcesBuffer.Dispose();
        groupArrBuffer.Dispose();
        hashDebugBuffer.Dispose();

        drawInstancedArgsBuffer.Dispose();
    }
}
