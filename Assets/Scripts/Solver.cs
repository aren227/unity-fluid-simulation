using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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

    public Mesh sphereMesh;

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
    private ComputeBuffer hashValueDebugBuffer;
    private ComputeBuffer meanBuffer;
    private ComputeBuffer principleBuffer;

    private ComputeBuffer quadInstancedArgsBuffer;
    private ComputeBuffer sphereInstancedArgsBuffer;

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

    private bool paused = true;
    private bool usePositionSmoothing = true;

    private CommandBuffer commandBuffer;
    private Mesh screenQuadMesh;

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
        wavePlanes[2] = GetPlaneEq(new Vector3(-50 + Mathf.Pow(Mathf.Sin(waveTime*0.1f),2) * 20f, 0, 0), Vector3.right);
        wavePlanes[3] = GetPlaneEq(new Vector3(50, 0, 0), Vector3.left);
        wavePlanes[4] = GetPlaneEq(new Vector3(0, 0, -50), Vector3.forward);
        wavePlanes[5] = GetPlaneEq(new Vector3(0, 0, 50), Vector3.back);

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

        globalHashCounterBuffer = new ComputeBuffer(numHashes, 4);

        localIndicesBuffer = new ComputeBuffer(numParticles, 4);

        inverseIndicesBuffer = new ComputeBuffer(numParticles, 4);

        particlesBuffer = new ComputeBuffer(numParticles, 4 * 8);
        particlesBuffer.SetData(particles);

        sortedBuffer = new ComputeBuffer(numParticles, 4 * 8);

        forcesBuffer = new ComputeBuffer(numParticles * 2, 4 * 4);

        int groupArrLen = Mathf.CeilToInt(numHashes / 1024f);
        groupArrBuffer = new ComputeBuffer(groupArrLen, 4);

        hashDebugBuffer = new ComputeBuffer(4, 4);
        hashValueDebugBuffer = new ComputeBuffer(numParticles, 4 * 3);

        meanBuffer = new ComputeBuffer(numParticles, 4 * 3);
        principleBuffer = new ComputeBuffer(numParticles * 4, 4 * 3);

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
            solverShader.SetBuffer(i, "mean", meanBuffer);
            solverShader.SetBuffer(i, "principle", principleBuffer);
            solverShader.SetBuffer(i, "hashValueDebug", hashValueDebugBuffer);
        }

        renderMat = new Material(renderShader);
        renderMat.SetBuffer("particles", particlesBuffer);
        renderMat.SetBuffer("principle", principleBuffer);
        renderMat.SetFloat("radius", particleRenderSize * 0.5f);

        quadInstancedArgsBuffer = new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);

        uint[] args = new uint[5];
        args[0] = particleMesh.GetIndexCount(0);
        args[1] = (uint) numParticles;
        args[2] = particleMesh.GetIndexStart(0);
        args[3] = particleMesh.GetBaseVertex(0);
        args[4] = 0;

        quadInstancedArgsBuffer.SetData(args);

        sphereInstancedArgsBuffer = new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);

        uint[] args2 = new uint[5];
        args2[0] = sphereMesh.GetIndexCount(0);
        args2[1] = (uint) numParticles;
        args2[2] = sphereMesh.GetIndexStart(0);
        args2[3] = sphereMesh.GetBaseVertex(0);
        args2[4] = 0;

        sphereInstancedArgsBuffer.SetData(args2);

        screenQuadMesh = new Mesh();
        screenQuadMesh.vertices = new Vector3[4] {
            new Vector3( 1.0f , 1.0f,  0.0f),
            new Vector3(-1.0f , 1.0f,  0.0f),
            new Vector3(-1.0f ,-1.0f,  0.0f),
            new Vector3( 1.0f ,-1.0f,  0.0f),
        };
        screenQuadMesh.uv = new Vector2[4] {
            new Vector2(1, 0),
            new Vector2(0, 0),
            new Vector2(0, 1),
            new Vector2(1, 1)
        };
        screenQuadMesh.triangles = new int[6] { 0, 1, 2, 2, 3, 0 };

        commandBuffer = new CommandBuffer();
        commandBuffer.name = "Fluid Render";

        UpdateCommandBuffer();
        Camera.main.AddCommandBuffer(CameraEvent.AfterForwardAlpha, commandBuffer);

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

            if (Input.GetKeyDown(KeyCode.Space)) {
                paused = !paused;
            }

            if (Input.GetKeyDown(KeyCode.Z)) {
                usePositionSmoothing = !usePositionSmoothing;
                Debug.Log("usePositionSmoothing: " + usePositionSmoothing);
            }

            renderMat.SetColor("primaryColor", primaryColor.linear);
            renderMat.SetColor("secondaryColor", secondaryColor.linear);
            renderMat.SetInt("usePositionSmoothing", usePositionSmoothing ? 1 : 0);

            double solverStart = Time.realtimeSinceStartupAsDouble;

            solverShader.Dispatch(solverShader.FindKernel("ResetCounter"), Mathf.CeilToInt((float)numHashes / numThreads), 1, 1);
            solverShader.Dispatch(solverShader.FindKernel("InsertToBucket"), Mathf.CeilToInt((float)numParticles / numThreads), 1, 1);

            // Debug
            if (Input.GetKeyDown(KeyCode.C)) {
                uint[] debugResult = new uint[4];

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

            // Debug
            if (Input.GetKeyDown(KeyCode.C)) {
                uint[] debugResult = new uint[4];

                int[] values = new int[numParticles * 3];

                hashDebugBuffer.SetData(debugResult);

                solverShader.Dispatch(solverShader.FindKernel("DebugHash"), Mathf.CeilToInt((float)numHashes / numThreads), 1, 1);

                hashDebugBuffer.GetData(debugResult);

                uint totalAccessCount = debugResult[2];
                uint totalNeighborCount = debugResult[3];

                Debug.Log($"Total access: {totalAccessCount}, Avg access: {(float)totalAccessCount / numParticles}, Avg accept: {(float)totalNeighborCount / numParticles}");
                Debug.Log($"Average accept rate: {(float)totalNeighborCount / totalAccessCount * 100}%");

                hashValueDebugBuffer.GetData(values);

                HashSet<Vector3Int> set = new HashSet<Vector3Int>();
                for (int i = 0; i < numParticles; i++) {
                    Vector3Int vi = new Vector3Int(values[i*3+0], values[i*3+1], values[i*3+2]);
                    set.Add(vi);
                }

                Debug.Log($"Total unique hash keys: {set.Count}, Ideal bucket load: {(float)set.Count / numHashes * 100}%");
            }

            if (!paused) {
                for (int iter = 0; iter < 1; iter++) {
                    solverShader.Dispatch(solverShader.FindKernel("CalcPressure"), Mathf.CeilToInt((float)numParticles / numThreads), 1, 1);
                    solverShader.Dispatch(solverShader.FindKernel("CalcForces"), Mathf.CeilToInt((float)numParticles / numThreads), 1, 1);
                    solverShader.Dispatch(solverShader.FindKernel("Step"), Mathf.CeilToInt((float)numParticles / numThreads), 1, 1);
                }

                solverFrame++;

                if (solverFrame > 1) {
                    totalFrameTime += Time.realtimeSinceStartupAsDouble - lastFrameTimestamp;
                }

                if (solverFrame == 400 || solverFrame == 1200) {
                    Debug.Log($"Avg frame time at #{solverFrame}: {totalFrameTime / (solverFrame-1) * 1000}ms.");
                }
            }

            lastFrameTimestamp = Time.realtimeSinceStartupAsDouble;
        }
    }

    void UpdateCommandBuffer() {
        commandBuffer.Clear();

        int[] worldPosBufferIds = new int[] {
            Shader.PropertyToID("worldPosBuffer1"),
            Shader.PropertyToID("worldPosBuffer2")
        };

        commandBuffer.GetTemporaryRT(worldPosBufferIds[0], Screen.width, Screen.height, 0, FilterMode.Point, RenderTextureFormat.ARGBFloat);
        commandBuffer.GetTemporaryRT(worldPosBufferIds[1], Screen.width, Screen.height, 0, FilterMode.Point, RenderTextureFormat.ARGBFloat);

        int depthId = Shader.PropertyToID("depthBuffer");
        commandBuffer.GetTemporaryRT(depthId, Screen.width, Screen.height, 32, FilterMode.Point, RenderTextureFormat.Depth);

        commandBuffer.SetRenderTarget((RenderTargetIdentifier)worldPosBufferIds[0], (RenderTargetIdentifier)depthId);
        commandBuffer.ClearRenderTarget(true, true, Color.clear);

        commandBuffer.DrawMeshInstancedIndirect(
            sphereMesh,
            0,  // submeshIndex
            renderMat,
            0,  // shaderPass
            sphereInstancedArgsBuffer
        );

        int depth2Id = Shader.PropertyToID("depth2Buffer");
        commandBuffer.GetTemporaryRT(depth2Id, Screen.width, Screen.height, 32, FilterMode.Point, RenderTextureFormat.Depth);

        commandBuffer.SetRenderTarget((RenderTargetIdentifier)worldPosBufferIds[0], (RenderTargetIdentifier)depth2Id);
        commandBuffer.ClearRenderTarget(true, true, Color.clear);

        commandBuffer.SetGlobalTexture("depthBuffer", depthId);

        commandBuffer.DrawMesh(
            screenQuadMesh,
            Matrix4x4.identity,
            renderMat,
            0, // submeshIndex
            1  // shaderPass
        );

        int normalBufferId = Shader.PropertyToID("normalBuffer");
        commandBuffer.GetTemporaryRT(normalBufferId, Screen.width, Screen.height, 0, FilterMode.Point, RenderTextureFormat.ARGBFloat);

        commandBuffer.SetRenderTarget((RenderTargetIdentifier)normalBufferId, (RenderTargetIdentifier)depth2Id);
        commandBuffer.ClearRenderTarget(false, true, Color.clear);

        commandBuffer.SetGlobalTexture("worldPosBuffer", worldPosBufferIds[0]);

        commandBuffer.DrawMeshInstancedIndirect(
            particleMesh,
            0,  // submeshIndex
            renderMat,
            3,  // shaderPass
            quadInstancedArgsBuffer
        );

        commandBuffer.SetGlobalTexture("normalBuffer", normalBufferId);

        int nextBuffer = 1;
        for (int iter = 0; iter < 0; iter++) {
            // Update world pos.
            commandBuffer.SetRenderTarget((RenderTargetIdentifier)worldPosBufferIds[nextBuffer], (RenderTargetIdentifier)depth2Id);

            commandBuffer.SetGlobalTexture("worldPosBuffer", worldPosBufferIds[nextBuffer^1]);

            commandBuffer.DrawMesh(
                screenQuadMesh,
                Matrix4x4.identity,
                renderMat,
                0, // submeshIndex
                2  // shaderPass
            );

            // Recalculate normals.
            commandBuffer.SetRenderTarget((RenderTargetIdentifier)normalBufferId, (RenderTargetIdentifier)depth2Id);
            commandBuffer.ClearRenderTarget(false, true, Color.clear);

            commandBuffer.SetGlobalTexture("worldPosBuffer", worldPosBufferIds[nextBuffer]);

            commandBuffer.DrawMeshInstancedIndirect(
                particleMesh,
                0,  // submeshIndex
                renderMat,
                3,  // shaderPass
                quadInstancedArgsBuffer
            );

            nextBuffer ^= 1;
        }

        commandBuffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);

        commandBuffer.DrawMesh(
            screenQuadMesh,
            Matrix4x4.identity,
            renderMat,
            0, // submeshIndex
            4  // shaderPass
        );
    }

    void LateUpdate() {
        Matrix4x4 view = Camera.main.worldToCameraMatrix;

        Shader.SetGlobalMatrix("inverseV", view.inverse);
        Shader.SetGlobalMatrix("inverseP", Camera.main.projectionMatrix.inverse);
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
        meanBuffer.Dispose();
        principleBuffer.Dispose();

        quadInstancedArgsBuffer.Dispose();
    }
}
