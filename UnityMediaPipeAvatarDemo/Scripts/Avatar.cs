using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class Avatar : MonoBehaviour
{
    public float handRotationSmoothing = 0.15f;
    public Camera previewCamera;
    public Animator animator;
    public LayerMask ground;
    public bool footTracking = true;
    public float footGroundOffset = .1f;
    
    [Header("Calibration")]
    public bool useCalibrationData = false;
    public PersistentCalibrationData calibrationData;
    
    [Header("Hand Settings")]
    public Vector3 handRotationCorrectionLeft = new Vector3(-5, -85, -80);
    public Vector3 handRotationCorrectionRight = new Vector3(-5, 85, 80);
    
    [Header("T-Pose Correction")]
    public bool applyTPoseCorrection = true;
    public Vector3 tPoseHandRotationLeft = new Vector3(0, 180, 0);
    public Vector3 tPoseHandRotationRight = new Vector3(0, 180, 0);
    
    [Header("Finger Settings")]
    public Vector3 fingerForwardAxis = Vector3.forward;
    public Vector3 fingerUpAxis = Vector3.up;
    [Range(0.1f, 1f)]
    public float fingerRotationWeight = 1f;
    public float fingerLengthThreshold = 0.02f;
    public float positionFilterThreshold = 0.001f;
    
    [Header("Debugging")]
    public bool enableLogging = true;
    public float loggingDuration = 10f;
    public KeyCode loggingKey = KeyCode.L;

    public bool Calibrated { get; private set; }

    private PipeServer server;
    private Quaternion initialRotation;
    private Vector3 initialPosition;
    private Quaternion targetRot;

    private Dictionary<HumanBodyBones, CalibrationData> parentCalibrationData = new Dictionary<HumanBodyBones, CalibrationData>();
    private CalibrationData spineUpDown, hipsTwist, chest, head;

    private Dictionary<HumanBodyBones, Vector3> previousFingerPositions = new Dictionary<HumanBodyBones, Vector3>();
    private Dictionary<HumanBodyBones, Quaternion> initialFingerRotations = new Dictionary<HumanBodyBones, Quaternion>();
    private Dictionary<string, Vector3> stablePositionsCache = new Dictionary<string, Vector3>();
    
    private StreamWriter logWriter;
    private float loggingStartTime;
    private bool isLogging = false;
    private int logFrameCount = 0;

    private void Start()
    {
        initialRotation = transform.rotation;
        initialPosition = transform.position;

        if (calibrationData && useCalibrationData)
        {
            CalibrateFromPersistent();
        }

        server = FindObjectOfType<PipeServer>();
        if (server == null)
        {
            Debug.LogError("You must have a PipeServer in the scene!");
        }
        
        InitializeFingerTracking();
        
        // Inicializar logging
        if (enableLogging)
        {
            SetupLogging();
        }
    }

    private void SetupLogging()
    {
        string logPath = Application.dataPath + "/HandMovementLog.json";
        logWriter = new StreamWriter(logPath, false);
        logWriter.WriteLine("{");
        logWriter.WriteLine("\"logEntries\": [");
        
        loggingStartTime = Time.time;
        isLogging = true;
        
        Debug.Log("Logging started: " + logPath);
    }

    private void StopLogging()
    {
        if (logWriter != null)
        {
            logWriter.WriteLine("]");
            logWriter.WriteLine("}");
            logWriter.Close();
            logWriter = null;
        }
        isLogging = false;
        Debug.Log("Logging stopped");
    }

    private void Update()
    {
        // Manejar logging
        if (Input.GetKeyDown(loggingKey))
        {
            if (isLogging)
            {
                StopLogging();
            }
            else
            {
                SetupLogging();
            }
        }
        
        if (isLogging && Time.time - loggingStartTime > loggingDuration)
        {
            StopLogging();
        }

        // Ground adjustment
        if (parentCalibrationData.Count > 0)
        {
            float displacement = 0;
            RaycastHit h1;
            if (Physics.Raycast(animator.GetBoneTransform(HumanBodyBones.LeftFoot).position, Vector3.down, out h1, 100f, ground, QueryTriggerInteraction.Ignore))
            {
                displacement = (h1.point - animator.GetBoneTransform(HumanBodyBones.LeftFoot).position).y;
            }
            if (Physics.Raycast(animator.GetBoneTransform(HumanBodyBones.RightFoot).position, Vector3.down, out h1, 100f, ground, QueryTriggerInteraction.Ignore))
            {
                float displacement2 = (h1.point - animator.GetBoneTransform(HumanBodyBones.RightFoot).position).y;
                if (Mathf.Abs(displacement2) < Mathf.Abs(displacement))
                {
                    displacement = displacement2;
                }
            }
            transform.position = Vector3.Lerp(transform.position, initialPosition + Vector3.up * displacement + Vector3.up * footGroundOffset,
                Time.deltaTime * 5f);
        }

        // Update limb rotations
        foreach (var i in parentCalibrationData)
        {
            Quaternion deltaRotTracked = Quaternion.FromToRotation(i.Value.initialDir, i.Value.CurrentDirection);
            i.Value.parent.rotation = deltaRotTracked * i.Value.initialRotation;
        }

        // Spine chain handling
        if (parentCalibrationData.Count > 0)
        {
            Vector3 hd = head.CurrentDirection;
            Quaternion headr = Quaternion.FromToRotation(head.initialDir, hd);
            Quaternion twist = Quaternion.FromToRotation(hipsTwist.initialDir,
                Vector3.Slerp(hipsTwist.initialDir, hipsTwist.CurrentDirection, .25f));
            Quaternion updown = Quaternion.FromToRotation(spineUpDown.initialDir,
                Vector3.Slerp(spineUpDown.initialDir, spineUpDown.CurrentDirection, .25f));

            Quaternion h = updown * updown * updown * twist * twist;
            Quaternion s = h * twist * updown;
            Quaternion c = s * twist * twist;
            float speed = 10f;
            hipsTwist.Tick(h * hipsTwist.initialRotation, speed);
            spineUpDown.Tick(s * spineUpDown.initialRotation, speed);
            chest.Tick(c * chest.initialRotation, speed);
            head.Tick(updown * twist * headr * head.initialRotation, speed);

            Vector3 d = Vector3.Slerp(hipsTwist.initialDir, hipsTwist.CurrentDirection, .25f);
            d.y *= 0.5f;
            Quaternion deltaRotTracked = Quaternion.FromToRotation(hipsTwist.initialDir, d);
            targetRot = deltaRotTracked * initialRotation;
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRot, Time.deltaTime * speed);
        }
        
        // Update hands only if we have valid data
        if (server != null && server.fingerInstances != null && server.fingerInstances.Count > 0)
        {
            UpdateHands();
        }
        else if (applyTPoseCorrection)
        {
            ApplyTPoseHandCorrection();
        }
    }

    private void InitializeFingerTracking()
    {
        // Mano izquierda
        StoreInitialFingerRotation(HumanBodyBones.LeftThumbProximal);
        StoreInitialFingerRotation(HumanBodyBones.LeftThumbIntermediate);
        StoreInitialFingerRotation(HumanBodyBones.LeftThumbDistal);
        StoreInitialFingerRotation(HumanBodyBones.LeftIndexProximal);
        StoreInitialFingerRotation(HumanBodyBones.LeftIndexIntermediate);
        StoreInitialFingerRotation(HumanBodyBones.LeftIndexDistal);
        StoreInitialFingerRotation(HumanBodyBones.LeftMiddleProximal);
        StoreInitialFingerRotation(HumanBodyBones.LeftMiddleIntermediate);
        StoreInitialFingerRotation(HumanBodyBones.LeftMiddleDistal);
        StoreInitialFingerRotation(HumanBodyBones.LeftRingProximal);
        StoreInitialFingerRotation(HumanBodyBones.LeftRingIntermediate);
        StoreInitialFingerRotation(HumanBodyBones.LeftRingDistal);
        StoreInitialFingerRotation(HumanBodyBones.LeftLittleProximal);
        StoreInitialFingerRotation(HumanBodyBones.LeftLittleIntermediate);
        StoreInitialFingerRotation(HumanBodyBones.LeftLittleDistal);
        
        // Mano derecha
        StoreInitialFingerRotation(HumanBodyBones.RightThumbProximal);
        StoreInitialFingerRotation(HumanBodyBones.RightThumbIntermediate);
        StoreInitialFingerRotation(HumanBodyBones.RightThumbDistal);
        StoreInitialFingerRotation(HumanBodyBones.RightIndexProximal);
        StoreInitialFingerRotation(HumanBodyBones.RightIndexIntermediate);
        StoreInitialFingerRotation(HumanBodyBones.RightIndexDistal);
        StoreInitialFingerRotation(HumanBodyBones.RightMiddleProximal);
        StoreInitialFingerRotation(HumanBodyBones.RightMiddleIntermediate);
        StoreInitialFingerRotation(HumanBodyBones.RightMiddleDistal);
        StoreInitialFingerRotation(HumanBodyBones.RightRingProximal);
        StoreInitialFingerRotation(HumanBodyBones.RightRingIntermediate);
        StoreInitialFingerRotation(HumanBodyBones.RightRingDistal);
        StoreInitialFingerRotation(HumanBodyBones.RightLittleProximal);
        StoreInitialFingerRotation(HumanBodyBones.RightLittleIntermediate);
        StoreInitialFingerRotation(HumanBodyBones.RightLittleDistal);
    }
    
    private void StoreInitialFingerRotation(HumanBodyBones bone)
    {
        Transform t = animator.GetBoneTransform(bone);
        if (t != null)
        {
            initialFingerRotations[bone] = t.rotation;
            previousFingerPositions[bone] = t.position;
        }
    }

    public void CalibrateFromPersistent()
    {
        parentCalibrationData.Clear();

        if (calibrationData)
        {
            foreach (PersistentCalibrationData.CalibrationEntry d in calibrationData.parentCalibrationData)
            {
                parentCalibrationData.Add(d.bone, d.data.ReconstructReferences());
            }
            spineUpDown = calibrationData.spineUpDown.ReconstructReferences();
            hipsTwist = calibrationData.hipsTwist.ReconstructReferences();
            chest = calibrationData.chest.ReconstructReferences();
            head = calibrationData.head.ReconstructReferences();
        }

        animator.enabled = false;
        Calibrated = true;
    }
    
    public void Calibrate()
    {
        Debug.Log("Calibrating on " + gameObject.name);
        parentCalibrationData.Clear();

        spineUpDown = new CalibrationData(animator.transform, animator.GetBoneTransform(HumanBodyBones.Spine), animator.GetBoneTransform(HumanBodyBones.Neck),
            server.GetVirtualHip(), server.GetVirtualNeck());
        hipsTwist = new CalibrationData(animator.transform, animator.GetBoneTransform(HumanBodyBones.Hips), animator.GetBoneTransform(HumanBodyBones.Hips),
            server.GetLandmark(Landmark.RIGHT_HIP), server.GetLandmark(Landmark.LEFT_HIP));
        chest = new CalibrationData(animator.transform, animator.GetBoneTransform(HumanBodyBones.Chest), animator.GetBoneTransform(HumanBodyBones.Chest),
            server.GetLandmark(Landmark.RIGHT_HIP), server.GetLandmark(Landmark.LEFT_HIP));
        head = new CalibrationData(animator.transform, animator.GetBoneTransform(HumanBodyBones.Neck), animator.GetBoneTransform(HumanBodyBones.Head),
            server.GetVirtualNeck(), server.GetLandmark(Landmark.NOSE));

        // Upper body
        AddCalibration(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
            server.GetLandmark(Landmark.RIGHT_SHOULDER), server.GetLandmark(Landmark.RIGHT_ELBOW));
        AddCalibration(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            server.GetLandmark(Landmark.RIGHT_ELBOW), server.GetLandmark(Landmark.RIGHT_WRIST));

        AddCalibration(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
            server.GetLandmark(Landmark.LEFT_SHOULDER), server.GetLandmark(Landmark.LEFT_ELBOW));
        AddCalibration(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            server.GetLandmark(Landmark.LEFT_ELBOW), server.GetLandmark(Landmark.LEFT_WRIST));

        // Lower body
        AddCalibration(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
            server.GetLandmark(Landmark.RIGHT_HIP), server.GetLandmark(Landmark.RIGHT_KNEE));
        AddCalibration(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
            server.GetLandmark(Landmark.RIGHT_KNEE), server.GetLandmark(Landmark.RIGHT_ANKLE));

        AddCalibration(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
            server.GetLandmark(Landmark.LEFT_HIP), server.GetLandmark(Landmark.LEFT_KNEE));
        AddCalibration(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            server.GetLandmark(Landmark.LEFT_KNEE), server.GetLandmark(Landmark.LEFT_ANKLE));

        if (footTracking)
        {
            AddCalibration(HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
                server.GetLandmark(Landmark.LEFT_ANKLE), server.GetLandmark(Landmark.LEFT_FOOT_INDEX));
            AddCalibration(HumanBodyBones.RightFoot, HumanBodyBones.RightToes,
                server.GetLandmark(Landmark.RIGHT_ANKLE), server.GetLandmark(Landmark.RIGHT_FOOT_INDEX));
        }

        animator.enabled = false;
        Calibrated = true;
    }

    public void StoreCalibration()
    {
        if (!calibrationData)
        {
            Debug.LogError("Optional calibration data must be assigned to store into.");
            return;
        }

        List<PersistentCalibrationData.CalibrationEntry> calibrations = new List<PersistentCalibrationData.CalibrationEntry>();
        foreach (KeyValuePair<HumanBodyBones, CalibrationData> k in parentCalibrationData)
        {
            calibrations.Add(new PersistentCalibrationData.CalibrationEntry() { bone = k.Key, data = k.Value });
        }
        calibrationData.parentCalibrationData = calibrations.ToArray();

        calibrationData.spineUpDown = spineUpDown;
        calibrationData.hipsTwist = hipsTwist;
        calibrationData.chest = chest;
        calibrationData.head = head;

        calibrationData.Dirty();
        Debug.Log("Completed storing calibration data " + calibrationData.name);
    }
    
    private void AddCalibration(HumanBodyBones parent, HumanBodyBones child, Transform trackParent, Transform trackChild)
    {
        parentCalibrationData.Add(parent,
            new CalibrationData(animator.transform, animator.GetBoneTransform(parent), animator.GetBoneTransform(child),
            trackParent, trackChild));
    }
    
    private void UpdateHands()
    {
        UpdateSingleHand(true);  // Left hand
        UpdateSingleHand(false); // Right hand
    }

    private void UpdateFingers(bool isLeft, Quaternion handRotation)
    {
        string prefix = isLeft ? "L" : "R";
        
        // Update en orden jerárquico
        UpdateFingerBone(prefix, "THUMB", 1, isLeft ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal, handRotation);
        UpdateFingerBone(prefix, "THUMB", 2, isLeft ? HumanBodyBones.LeftThumbIntermediate : HumanBodyBones.RightThumbIntermediate, handRotation);
        UpdateFingerBone(prefix, "THUMB", 3, isLeft ? HumanBodyBones.LeftThumbDistal : HumanBodyBones.RightThumbDistal, handRotation);
        
        UpdateFingerBone(prefix, "INDEX", 1, isLeft ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal, handRotation);
        UpdateFingerBone(prefix, "INDEX", 2, isLeft ? HumanBodyBones.LeftIndexIntermediate : HumanBodyBones.RightIndexIntermediate, handRotation);
        UpdateFingerBone(prefix, "INDEX", 3, isLeft ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal, handRotation);
        
        UpdateFingerBone(prefix, "MIDDLE", 1, isLeft ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal, handRotation);
        UpdateFingerBone(prefix, "MIDDLE", 2, isLeft ? HumanBodyBones.LeftMiddleIntermediate : HumanBodyBones.RightMiddleIntermediate, handRotation);
        UpdateFingerBone(prefix, "MIDDLE", 3, isLeft ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal, handRotation);
        
        UpdateFingerBone(prefix, "RING", 1, isLeft ? HumanBodyBones.LeftRingProximal : HumanBodyBones.RightRingProximal, handRotation);
        UpdateFingerBone(prefix, "RING", 2, isLeft ? HumanBodyBones.LeftRingIntermediate : HumanBodyBones.RightRingIntermediate, handRotation);
        UpdateFingerBone(prefix, "RING", 3, isLeft ? HumanBodyBones.LeftRingDistal : HumanBodyBones.RightRingDistal, handRotation);
        
        UpdateFingerBone(prefix, "PINKY", 1, isLeft ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal, handRotation);
        UpdateFingerBone(prefix, "PINKY", 2, isLeft ? HumanBodyBones.LeftLittleIntermediate : HumanBodyBones.RightLittleIntermediate, handRotation);
        UpdateFingerBone(prefix, "PINKY", 3, isLeft ? HumanBodyBones.LeftLittleDistal : HumanBodyBones.RightLittleDistal, handRotation);
    }

    private void UpdateFingerBone(string prefix, string finger, int segment, HumanBodyBones bone, Quaternion handRotation)
    {
        string baseKey = $"{prefix}_{finger}_{segment}";
        string tipKey = $"{prefix}_{finger}_{segment + 1}";
        
        // Verificación de existencia
        if (server.fingerInstances == null || 
            !server.fingerInstances.ContainsKey(baseKey) || 
            !server.fingerInstances[baseKey].activeSelf ||
            !server.fingerInstances.ContainsKey(tipKey) || 
            !server.fingerInstances[tipKey].activeSelf)
        {
            return;
        }

        // Obtener posiciones estables
        Vector3 basePos = GetStablePosition(baseKey);
        Vector3 tipPos = GetStablePosition(tipKey);
        
        // Filtrar movimientos mínimos
        float boneLength = Vector3.Distance(basePos, tipPos);
        if (boneLength < fingerLengthThreshold)
        {
            return;
        }

        Transform boneTransform = animator.GetBoneTransform(bone);
        if (boneTransform == null) return;

        // 1. Calcular dirección del hueso
        Vector3 boneDirection = (tipPos - basePos).normalized;
        
        // 2. Sistema de coordenadas mejorado
        Vector3 referenceRight = Vector3.Cross(boneDirection, handRotation * Vector3.up).normalized;
        if (referenceRight.magnitude < 0.1f)
        {
            referenceRight = handRotation * Vector3.right;
        }
        
        Vector3 referenceUp = Vector3.Cross(referenceRight, boneDirection).normalized;
        
        // 3. Calcular rotación objetivo
        Quaternion targetRotation = Quaternion.LookRotation(boneDirection, referenceUp);
        
        // 4. Aplicar correcciones específicas por tipo de dedo
        Quaternion anatomicalCorrection = Quaternion.identity;
        
        if (finger == "THUMB")
        {
            anatomicalCorrection = Quaternion.Euler(
                prefix == "L" ? new Vector3(0, 30, -15) : new Vector3(0, -30, 15)
            );
        }
        else
        {
            anatomicalCorrection = Quaternion.Euler(
                prefix == "L" ? new Vector3(-10, 0, 0) : new Vector3(-10, 0, 0)
            );
        }
        
        Quaternion finalRotation = targetRotation * anatomicalCorrection;

        // 5. Usar rotación inicial como base
        Quaternion baseRotation = initialFingerRotations.ContainsKey(bone) ? 
            initialFingerRotations[bone] : boneTransform.rotation;
        
        // 6. Calcular factor de suavizado dinámico
        float smoothFactor = CalculateSmoothFactor(bone, tipPos);
        
        // 7. Aplicar rotación
        Quaternion currentRotation = boneTransform.rotation;
        boneTransform.rotation = Quaternion.Slerp(
            currentRotation,
            finalRotation,
            handRotationSmoothing * fingerRotationWeight * smoothFactor
        );
        
        // Loggear datos del dedo
        if (isLogging)
        {
            LogFingerData(prefix, finger, segment, bone, 
                basePos, tipPos, boneDirection, 
                targetRotation, anatomicalCorrection, finalRotation, 
                currentRotation, boneTransform.rotation);
        }
        
        // Actualizar posición previa
        previousFingerPositions[bone] = tipPos;
    }

    private void LogFingerData(string prefix, string finger, int segment, HumanBodyBones bone,
                             Vector3 basePos, Vector3 tipPos, Vector3 boneDirection,
                             Quaternion targetRotation, Quaternion anatomicalCorrection, 
                             Quaternion finalRotation, Quaternion currentRotation, 
                             Quaternion appliedRotation)
    {
        if (!isLogging || logWriter == null) return;
        
        if (logFrameCount > 0)
        {
            logWriter.WriteLine(",");
        }
        
        string boneName = $"{prefix}_{finger}_{segment}";
        string json = $@"    {{
        ""frame"": {Time.frameCount},
        ""time"": {Time.time:F3},
        ""bone"": ""{boneName}"",
        ""boneType"": ""{bone}"",
        ""positions"": {{
            ""base"": {{ ""x"": {basePos.x:F4}, ""y"": {basePos.y:F4}, ""z"": {basePos.z:F4} }},
            ""tip"": {{ ""x"": {tipPos.x:F4}, ""y"": {tipPos.y:F4}, ""z"": {tipPos.z:F4} }}
        }},
        ""direction"": {{ ""x"": {boneDirection.x:F4}, ""y"": {boneDirection.y:F4}, ""z"": {boneDirection.z:F4} }},
        ""rotations"": {{
            ""target"": {{ ""x"": {targetRotation.x:F4}, ""y"": {targetRotation.y:F4}, ""z"": {targetRotation.z:F4}, ""w"": {targetRotation.w:F4} }},
            ""correction"": {{ ""x"": {anatomicalCorrection.x:F4}, ""y"": {anatomicalCorrection.y:F4}, ""z"": {anatomicalCorrection.z:F4}, ""w"": {anatomicalCorrection.w:F4} }},
            ""final"": {{ ""x"": {finalRotation.x:F4}, ""y"": {finalRotation.y:F4}, ""z"": {finalRotation.z:F4}, ""w"": {finalRotation.w:F4} }},
            ""current"": {{ ""x"": {currentRotation.x:F4}, ""y"": {currentRotation.y:F4}, ""z"": {currentRotation.z:F4}, ""w"": {currentRotation.w:F4} }},
            ""applied"": {{ ""x"": {appliedRotation.x:F4}, ""y"": {appliedRotation.y:F4}, ""z"": {appliedRotation.z:F4}, ""w"": {appliedRotation.w:F4} }}
        }}
    }}";
        
        logWriter.Write(json);
        logFrameCount++;
    }

    private float CalculateSmoothFactor(HumanBodyBones bone, Vector3 currentPos)
    {
        if (!previousFingerPositions.ContainsKey(bone))
            return 1f;
            
        float distance = Vector3.Distance(previousFingerPositions[bone], currentPos);
        float speed = distance / Time.deltaTime;
        
        // Movimientos rápidos requieren menos suavizado
        return Mathf.Clamp(speed * 0.5f, 0.7f, 2f);
    }

    private void ApplyTPoseHandCorrection()
    {
        ApplyHandTPoseCorrection(true);
        ApplyHandTPoseCorrection(false);
    }

    private void ApplyHandTPoseCorrection(bool isLeft)
    {
        Transform handTransform = animator.GetBoneTransform(
            isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand
        );

        if (handTransform == null) return;

        Quaternion correction = Quaternion.Euler(isLeft ? 
            tPoseHandRotationLeft : tPoseHandRotationRight);

        handTransform.localRotation = correction;
    }

    private bool IsInTPose()
    {
        // Comprobar si los brazos están extendidos (posición T-pose)
        Vector3 leftArmDir = animator.GetBoneTransform(HumanBodyBones.LeftHand).position - 
                            animator.GetBoneTransform(HumanBodyBones.LeftShoulder).position;
        Vector3 rightArmDir = animator.GetBoneTransform(HumanBodyBones.RightHand).position - 
                             animator.GetBoneTransform(HumanBodyBones.RightShoulder).position;

        float angleLeft = Vector3.Angle(leftArmDir, Vector3.left);
        float angleRight = Vector3.Angle(rightArmDir, Vector3.right);

        return angleLeft < 15f && angleRight < 15f;
    }

    private void UpdateSingleHand(bool isLeft)
    {
        string prefix = isLeft ? "L" : "R";
        Transform handTransform = animator.GetBoneTransform(
            isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand
        );

        string wristKey = $"{prefix}_WRIST";
        string indexKey = $"{prefix}_INDEX_1";
        string pinkyKey = $"{prefix}_PINKY_1";
        string middleKey = $"{prefix}_MIDDLE_1";
        string thumbKey = $"{prefix}_THUMB_2"; // Usar THUMB_2 para mejor estabilidad
        
        // Validar puntos requeridos
        if (server == null || server.fingerInstances == null) return;
        
        bool validPoints = 
            server.fingerInstances.ContainsKey(wristKey) && server.fingerInstances[wristKey].activeSelf &&
            server.fingerInstances.ContainsKey(indexKey) && server.fingerInstances[indexKey].activeSelf &&
            server.fingerInstances.ContainsKey(pinkyKey) && server.fingerInstances[pinkyKey].activeSelf &&
            server.fingerInstances.ContainsKey(middleKey) && server.fingerInstances[middleKey].activeSelf &&
            server.fingerInstances.ContainsKey(thumbKey) && server.fingerInstances[thumbKey].activeSelf;
        
        if (!validPoints) return;

        // Obtener posiciones estables
        Vector3 wrist = GetStablePosition(wristKey);
        Vector3 index = GetStablePosition(indexKey);
        Vector3 pinky = GetStablePosition(pinkyKey);
        Vector3 middle = GetStablePosition(middleKey);
        Vector3 thumb = GetStablePosition(thumbKey);

        // 1. CALCULO MEJORADO DEL PLANO DE LA MANO
        Vector3 toIndex = (index - wrist);
        Vector3 toPinky = (pinky - wrist);
        
        Vector3 handPlaneNormal = Vector3.Cross(toIndex, toPinky).normalized;
        if (handPlaneNormal.magnitude < 0.1f)
        {
            handPlaneNormal = handTransform.up;
        }
        
        // 2. CALCULAR VECTORES DE ORIENTACIÓN
        Vector3 handForward = (middle - wrist).normalized;
        Vector3 handRight = Vector3.Cross(handPlaneNormal, handForward).normalized;
        Vector3 handUp = Vector3.Cross(handForward, handRight).normalized;
        
        // 3. COMPROBAR Y CORREGIR ORTOGONALIDAD
        if (Mathf.Abs(Vector3.Dot(handForward, handUp)) > 0.1f)
        {
            // Si no son ortogonales, recalcular usando el pulgar como referencia
            Vector3 thumbDirection = (thumb - wrist).normalized;
            handUp = Vector3.Cross(handForward, thumbDirection).normalized;
            handRight = Vector3.Cross(handUp, handForward).normalized;
        }
        
        // 4. ROTACIÓN DE PALMA CON CORRECCIÓN MEJORADA
        Quaternion palmRotation = Quaternion.LookRotation(handForward, handUp);
        
        // 5. APLICAR CORRECCIÓN MANUAL
        Quaternion correction = Quaternion.Euler(isLeft ? 
            handRotationCorrectionLeft : handRotationCorrectionRight);
        
        // Aplicar corrección adicional en T-pose si es necesario
        if (applyTPoseCorrection && IsInTPose())
        {
            correction *= Quaternion.Euler(isLeft ? 
                tPoseHandRotationLeft : tPoseHandRotationRight);
        }
        
        Quaternion finalHandRotation = palmRotation * correction;
        
        // 6. OBTENER ROTACIÓN ACTUAL
        Quaternion currentHandRotation = handTransform.rotation;
        
        // 7. CALCULAR FACTOR DE SUAVIZADO
        float handSmooth = CalculateHandSmoothFactor(handTransform.position, wrist);
        
        // 8. APLICAR ROTACIÓN CON SUAVIZADO
        handTransform.rotation = Quaternion.Slerp(
            currentHandRotation,
            finalHandRotation,
            handRotationSmoothing * handSmooth
        );
        
        // Loggear datos de la mano
        if (isLogging)
        {
            LogHandData(prefix, wrist, index, pinky, middle, thumb, 
                        handPlaneNormal, handForward, handUp, (thumb - wrist).normalized,
                        palmRotation, correction, finalHandRotation, 
                        currentHandRotation, handTransform.rotation);
        }
        
        // Actualizar dedos
        UpdateFingers(isLeft, finalHandRotation);
    }
    
    private void LogHandData(string prefix, Vector3 wrist, Vector3 index, Vector3 pinky, Vector3 middle, Vector3 thumb,
                           Vector3 handPlaneNormal, Vector3 handForward, Vector3 handUp, Vector3 thumbDirection,
                           Quaternion palmRotation, Quaternion correction, Quaternion finalHandRotation,
                           Quaternion currentHandRotation, Quaternion appliedHandRotation)
    {
        if (!isLogging || logWriter == null) return;
        
        if (logFrameCount > 0)
        {
            logWriter.WriteLine(",");
        }
        
        string json = $@"    {{
        ""frame"": {Time.frameCount},
        ""time"": {Time.time:F3},
        ""hand"": ""{prefix}"",
        ""positions"": {{
            ""wrist"": {{ ""x"": {wrist.x:F4}, ""y"": {wrist.y:F4}, ""z"": {wrist.z:F4} }},
            ""index"": {{ ""x"": {index.x:F4}, ""y"": {index.y:F4}, ""z"": {index.z:F4} }},
            ""pinky"": {{ ""x"": {pinky.x:F4}, ""y"": {pinky.y:F4}, ""z"": {pinky.z:F4} }},
            ""middle"": {{ ""x"": {middle.x:F4}, ""y"": {middle.y:F4}, ""z"": {middle.z:F4} }},
            ""thumb"": {{ ""x"": {thumb.x:F4}, ""y"": {thumb.y:F4}, ""z"": {thumb.z:F4} }}
        }},
        ""vectors"": {{
            ""planeNormal"": {{ ""x"": {handPlaneNormal.x:F4}, ""y"": {handPlaneNormal.y:F4}, ""z"": {handPlaneNormal.z:F4} }},
            ""forward"": {{ ""x"": {handForward.x:F4}, ""y"": {handForward.y:F4}, ""z"": {handForward.z:F4} }},
            ""up"": {{ ""x"": {handUp.x:F4}, ""y"": {handUp.y:F4}, ""z"": {handUp.z:F4} }},
            ""thumbDirection"": {{ ""x"": {thumbDirection.x:F4}, ""y"": {thumbDirection.y:F4}, ""z"": {thumbDirection.z:F4} }}
        }},
        ""rotations"": {{
            ""palm"": {{ ""x"": {palmRotation.x:F4}, ""y"": {palmRotation.y:F4}, ""z"": {palmRotation.z:F4}, ""w"": {palmRotation.w:F4} }},
            ""correction"": {{ ""x"": {correction.x:F4}, ""y"": {correction.y:F4}, ""z"": {correction.z:F4}, ""w"": {correction.w:F4} }},
            ""final"": {{ ""x"": {finalHandRotation.x:F4}, ""y"": {finalHandRotation.y:F4}, ""z"": {finalHandRotation.z:F4}, ""w"": {finalHandRotation.w:F4} }},
            ""current"": {{ ""x"": {currentHandRotation.x:F4}, ""y"": {currentHandRotation.y:F4}, ""z"": {currentHandRotation.z:F4}, ""w"": {currentHandRotation.w:F4} }},
            ""applied"": {{ ""x"": {appliedHandRotation.x:F4}, ""y"": {appliedHandRotation.y:F4}, ""z"": {appliedHandRotation.z:F4}, ""w"": {appliedHandRotation.w:F4} }}
        }}
    }}";
        
        logWriter.Write(json);
        logFrameCount++;
    }

    private Vector3 GetStablePosition(string key)
    {
        Vector3 currentPos = server.fingerInstances[key].transform.position;
        
        // Filtrar micro-movimientos
        if (stablePositionsCache.ContainsKey(key))
        {
            float distance = Vector3.Distance(stablePositionsCache[key], currentPos);
            if (distance < positionFilterThreshold)
            {
                return stablePositionsCache[key];
            }
        }
        
        stablePositionsCache[key] = currentPos;
        return currentPos;
    }
    
    private float CalculateHandSmoothFactor(Vector3 handPos, Vector3 wristPos)
    {
        float distance = Vector3.Distance(handPos, wristPos);
        return Mathf.Clamp(distance * 15f, 0.9f, 2f);
    }

    private void OnDestroy()
    {
        if (isLogging)
        {
            StopLogging();
        }
    }
}