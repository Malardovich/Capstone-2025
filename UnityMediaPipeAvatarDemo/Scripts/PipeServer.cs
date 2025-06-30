using System.Collections;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Video;

[DefaultExecutionOrder(-1)]
public class PipeServer : MonoBehaviour
{
    public Dictionary<string, GameObject> fingerInstances = new Dictionary<string, GameObject>();
    private Dictionary<string, LineRenderer> handLines = new Dictionary<string, LineRenderer>();
    private readonly string[] fingerKeys = new string[]
    {
        "L_WRIST","L_THUMB_1","L_THUMB_2","L_THUMB_3","L_THUMB_4",
        "L_INDEX_1","L_INDEX_2","L_INDEX_3","L_INDEX_4",
        "L_MIDDLE_1","L_MIDDLE_2","L_MIDDLE_3","L_MIDDLE_4",
        "L_RING_1","L_RING_2","L_RING_3","L_RING_4",
        "L_PINKY_1","L_PINKY_2","L_PINKY_3","L_PINKY_4",
        "R_WRIST","R_THUMB_1","R_THUMB_2","R_THUMB_3","R_THUMB_4",
        "R_INDEX_1","R_INDEX_2","R_INDEX_3","R_INDEX_4",
        "R_MIDDLE_1","R_MIDDLE_2","R_MIDDLE_3","R_MIDDLE_4",
        "R_RING_1","R_RING_2","R_RING_3","R_RING_4",
        "R_PINKY_1","R_PINKY_2","R_PINKY_3","R_PINKY_4"
    };

    private readonly string[][] handConnections = new string[][]
    {
        new string[] { "L_WRIST", "L_THUMB_1", "L_THUMB_2", "L_THUMB_3", "L_THUMB_4" },
        new string[] { "R_WRIST", "R_THUMB_1", "R_THUMB_2", "R_THUMB_3", "R_THUMB_4" },
        new string[] { "L_WRIST", "L_INDEX_1", "L_INDEX_2", "L_INDEX_3", "L_INDEX_4" },
        new string[] { "R_WRIST", "R_INDEX_1", "R_INDEX_2", "R_INDEX_3", "R_INDEX_4" },
        new string[] { "L_WRIST", "L_MIDDLE_1", "L_MIDDLE_2", "L_MIDDLE_3", "L_MIDDLE_4" },
        new string[] { "R_WRIST", "R_MIDDLE_1", "R_MIDDLE_2", "R_MIDDLE_3", "R_MIDDLE_4" },
        new string[] { "L_WRIST", "L_RING_1", "L_RING_2", "L_RING_3", "L_RING_4" },
        new string[] { "R_WRIST", "R_RING_1", "R_RING_2", "R_RING_3", "R_RING_4" },
        new string[] { "L_WRIST", "L_PINKY_1", "L_PINKY_2", "L_PINKY_3", "L_PINKY_4" },
        new string[] { "R_WRIST", "R_PINKY_1", "R_PINKY_2", "R_PINKY_3", "R_PINKY_4" }
    };

    private Dictionary<string, AccumulatedBuffer> fingerBuffers = new Dictionary<string, AccumulatedBuffer>();
    private Dictionary<string, Vector3> fingerTargetOffsets = new Dictionary<string, Vector3>();
    private Dictionary<string, Vector3> fingerCurrentOffsets = new Dictionary<string, Vector3>();
    private readonly object fingerLock = new object();

    public bool useLegacyPipes = false;
    public string host = "127.0.0.1";
    public int port = 52733;
    public Transform bodyParent;
    public GameObject landmarkPrefab;
    public GameObject linePrefab;
    public GameObject headPrefab;
    public bool enableHead = false;
    public float multiplier = 1f;
    public float bodyLandmarkScale = 0.05f; // Escala de los landmarks del cuerpo
    public float handScaleMultiplier = 0.05f; // Escala de los landmarks de la mano (multiplicador sobre el cuerpo)
    public float maxSpeed = 50f;
    public float debug_samplespersecond;
    public int samplesForPose = 3; // Mejora: más muestras para suavizar
    public bool active;
    public float handLineWidthMultiplier = 0.05f; // Grosor de líneas de manos
    public float handLengthMultiplier = 0.5f; // Tamaño de las manos (líneas y puntos)

    private NamedPipeServerStream serverNP;
    private BinaryReader reader;
    private ServerUDP server;

    private Body body;
    private Transform virtualNeck;
    private Transform virtualHip;

    // --- VIDEO PLAYER INTEGRATION ---
    public VideoPlayer videoPlayer; // Asigna el VideoPlayer en el inspector
    private bool videoStarted = false;
    private bool requestVideoPlay = false;
    // ---------------------------------

    public Transform GetLandmark(Landmark mark) => body.instances[(int)mark].transform;
    public Transform GetVirtualNeck() => virtualNeck;
    public Transform GetVirtualHip() => virtualHip;

    private void Start()
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

        // Usa bodyLandmarkScale para el cuerpo
        body = new Body(bodyParent, landmarkPrefab, linePrefab, bodyLandmarkScale, enableHead ? headPrefab : null, handLineWidthMultiplier);
        virtualNeck = new GameObject("VirtualNeck").transform;
        virtualHip = new GameObject("VirtualHip").transform;
        virtualNeck.parent = bodyParent;
        virtualHip.parent = bodyParent;

        // Usa handScaleMultiplier para las manos
        float handLandmarkScale = bodyLandmarkScale * handScaleMultiplier;
        Debug.Log("Escala de landmark de mano: " + handLandmarkScale);

        foreach (var key in fingerKeys)
        {
            var go = Instantiate(landmarkPrefab);
            go.transform.localScale = Vector3.one * handLandmarkScale;
            go.name = key;
            go.transform.parent = bodyParent;
            fingerInstances[key] = go;

            fingerBuffers[key] = new AccumulatedBuffer(Vector3.zero, 0);
            fingerTargetOffsets[key] = Vector3.zero;
            fingerCurrentOffsets[key] = Vector3.zero;
        }

        for (int i = 0; i < handConnections.Length; i++)
        {
            var line = Instantiate(linePrefab).GetComponent<LineRenderer>();
            line.transform.parent = bodyParent;
            handLines["handLine" + i] = line;
        }

        Thread t = new Thread(new ThreadStart(Run));
        t.Start();
    }

    private void Update()
    {
        UpdateBody(body);
        UpdateHands();
        UpdateHandLines();

        // Solo aquí accede a videoPlayer y videoStarted
        if (requestVideoPlay && !videoStarted && videoPlayer != null)
        {
            videoPlayer.Play();
            videoStarted = true;
            requestVideoPlay = false;
        }
    }

    private void UpdateHandLines()
    {
        int lineIdx = 0;
        foreach (var conn in handConnections)
        {
            var line = handLines["handLine" + lineIdx];
            bool allActive = true;
            Vector3[] positions = new Vector3[conn.Length];

            for (int j = 0; j < conn.Length; j++)
            {
                if (fingerInstances.TryGetValue(conn[j], out var go) && go.activeSelf)
                {
                    positions[j] = go.transform.position;
                }
                else
                {
                    allActive = false;
                    break;
                }
            }

            line.positionCount = allActive ? conn.Length : 0;
            if (allActive) line.SetPositions(positions);
            lineIdx++;
        }
    }

    private void UpdateHands()
    {
        lock (fingerLock)
        {
            foreach (var key in fingerKeys)
            {
                var buffer = fingerBuffers[key];
                if (buffer.accumulatedValuesCount >= samplesForPose)
                {
                    // CORRECCIÓN PARA MODO ESPEJO: Invertir el eje X
                    Vector3 correctedValue = buffer.value / buffer.accumulatedValuesCount;
                    correctedValue.x = -correctedValue.x;
                    
                    fingerTargetOffsets[key] = correctedValue * multiplier;
                    fingerBuffers[key] = new AccumulatedBuffer(Vector3.zero, 0);
                }
            }
        }

        foreach (var key in fingerKeys)
        {
            // Mejora: usa Lerp para suavizar más el movimiento
            fingerCurrentOffsets[key] = Vector3.Lerp(
                fingerCurrentOffsets[key],
                fingerTargetOffsets[key],
                Time.deltaTime * maxSpeed * 0.5f // Ajusta el factor para más/menos suavidad
            );

            string wristKey = key.StartsWith("L_") ? "L_WRIST" : "R_WRIST";
            Vector3 wristWorld = wristKey == "L_WRIST" ?
                body.instances[(int)Landmark.LEFT_WRIST].transform.position :
                body.instances[(int)Landmark.RIGHT_WRIST].transform.position;

            fingerInstances[key].SetActive(true);

            // --- Escala SOLO los dedos, no la muñeca ---
            if (key.EndsWith("_WRIST"))
            {
                fingerInstances[key].transform.position = wristWorld;
            }
            else
            {
                fingerInstances[key].transform.position = wristWorld + fingerCurrentOffsets[key] * handLengthMultiplier;
            }
            // -------------------------------------------
        }
    }

    private void UpdateBody(Body b)
    {
        for (int i = 0; i < LANDMARK_COUNT; ++i)
        {
            if (b.positionsBuffer[i].accumulatedValuesCount < samplesForPose)
                continue;

            b.localPositionTargets[i] = b.positionsBuffer[i].value /
                (float)b.positionsBuffer[i].accumulatedValuesCount * multiplier;
            b.positionsBuffer[i] = new AccumulatedBuffer(Vector3.zero, 0);
        }

        Vector3 offset = Vector3.zero;
        for (int i = 0; i < LANDMARK_COUNT; ++i)
        {
            Vector3 p = b.localPositionTargets[i] - offset;
            b.instances[i].transform.localPosition = Vector3.MoveTowards(
                b.instances[i].transform.localPosition,
                p,
                Time.deltaTime * maxSpeed
            );
        }

        virtualNeck.position = (b.instances[(int)Landmark.RIGHT_SHOULDER].transform.position +
                              b.instances[(int)Landmark.LEFT_SHOULDER].transform.position) / 2f;
        virtualHip.position = (b.instances[(int)Landmark.RIGHT_HIP].transform.position +
                             b.instances[(int)Landmark.LEFT_HIP].transform.position) / 2f;

        b.UpdateLines();
    }

    // Cambiado: nunca ocultes el modelo de landmarks
    public void SetVisible(bool visible)
    {
        // Nunca ocultes el modelo de landmarks
        if (!visible) return;
        bodyParent.gameObject.SetActive(true);
    }

    private void Run()
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

        if (useLegacyPipes)
        {
            serverNP = new NamedPipeServerStream("UnityMediaPipeBody1", PipeDirection.InOut, 99, PipeTransmissionMode.Message);
            print("Waiting for connection...");
            serverNP.WaitForConnection();
            print("Connected.");
            reader = new BinaryReader(serverNP, Encoding.UTF8);
        }
        else
        {
            server = new ServerUDP(host, port);
            server.Connect();
            server.StartListeningAsync();
            print($"Listening @ {host}:{port}");
        }

        while (true)
        {
            try
            {
                Body h = body;
                string str = "";

                if (useLegacyPipes)
                {
                    var len = (int)reader.ReadUInt32();
                    str = new string(reader.ReadChars(len));
                }
                else if (server.HasMessage())
                {
                    str = server.GetMessage();
                }

                if (string.IsNullOrEmpty(str))
                {
                    Thread.Sleep(10);
                    continue;
                }

                ProcessData(str, h);
            }
            catch (EndOfStreamException)
            {
                print("Client Disconnected");
                break;
            }
        }
    }

    private void ProcessData(string data, Body body)
    {
        // --- VIDEO PLAYER INTEGRATION ---
        if (!string.IsNullOrEmpty(data))
        {
            requestVideoPlay = true;
        }
        // ---------------------------------

        string[] lines = data.Split('\n');
        foreach (string l in lines)
        {
            if (string.IsNullOrWhiteSpace(l)) continue;

            string[] s = l.Split('|');
            if (s.Length >= 6 && s[0] == "FINGER")
            {
                ProcessFingerData(s);
            }
            else if (s.Length >= 4)
            {
                ProcessBodyLandmark(s, body);
            }
        }
    }

    private void ProcessFingerData(string[] data)
    {
        string hand = data[1];
        string idx = data[2];
        float x = float.Parse(data[3]);
        float y = float.Parse(data[4]);
        float z = float.Parse(data[5]);

        string key = GetFingerKey(hand, idx);

        lock (fingerLock)
        {
            var buffer = fingerBuffers[key];
            buffer.value += new Vector3(x, y, z);
            buffer.accumulatedValuesCount += 1;
            fingerBuffers[key] = buffer;
        }
    }

    private string GetFingerKey(string hand, string idx)
    {
        switch (idx)
        {
            case "0": return $"{hand}_WRIST";
            case "1": return $"{hand}_THUMB_1";
            case "2": return $"{hand}_THUMB_2";
            case "3": return $"{hand}_THUMB_3";
            case "4": return $"{hand}_THUMB_4";
            case "5": return $"{hand}_INDEX_1";
            case "6": return $"{hand}_INDEX_2";
            case "7": return $"{hand}_INDEX_3";
            case "8": return $"{hand}_INDEX_4";
            case "9": return $"{hand}_MIDDLE_1";
            case "10": return $"{hand}_MIDDLE_2";
            case "11": return $"{hand}_MIDDLE_3";
            case "12": return $"{hand}_MIDDLE_4";
            case "13": return $"{hand}_RING_1";
            case "14": return $"{hand}_RING_2";
            case "15": return $"{hand}_RING_3";
            case "16": return $"{hand}_RING_4";
            case "17": return $"{hand}_PINKY_1";
            case "18": return $"{hand}_PINKY_2";
            case "19": return $"{hand}_PINKY_3";
            case "20": return $"{hand}_PINKY_4";
            default: return $"{hand}_{idx}";
        }
    }

    private void ProcessBodyLandmark(string[] data, Body body)
    {
        Debug.Log("ProcessBodyLandmark ejecutado");
        if (!int.TryParse(data[0], out int i)) return;

        body.positionsBuffer[i].value += new Vector3(
            float.Parse(data[1]),
            float.Parse(data[2]),
            float.Parse(data[3])
        );
        body.positionsBuffer[i].accumulatedValuesCount += 1;
        body.active = true;
        this.active = true;
    }

    private void OnDisable()
    {
        print("Client disconnected.");
        if (useLegacyPipes)
        {
            serverNP?.Close();
            serverNP?.Dispose();
        }
        else
        {
            server?.Disconnect();
        }
        bodyParent?.gameObject.SetActive(true);
    }

    const int LANDMARK_COUNT = 33;
    const int LINES_COUNT = 11;

    public struct AccumulatedBuffer
    {
        public Vector3 value;
        public int accumulatedValuesCount;
        public AccumulatedBuffer(Vector3 v, int ac) => (value, accumulatedValuesCount) = (v, ac);
    }

    public class Body
    {
        public Transform parent;
        public AccumulatedBuffer[] positionsBuffer = new AccumulatedBuffer[LANDMARK_COUNT];
        public Vector3[] localPositionTargets = new Vector3[LANDMARK_COUNT];
        public GameObject[] instances = new GameObject[LANDMARK_COUNT];
        public LineRenderer[] lines = new LineRenderer[LINES_COUNT];
        public bool active;

        public Body(Transform parent, GameObject landmarkPrefab, GameObject linePrefab, float s, GameObject headPrefab, float handLineWidthMultiplier)
        {
            this.parent = parent;
            for (int i = 0; i < instances.Length; ++i)
            {
                instances[i] = Instantiate(landmarkPrefab);
                instances[i].transform.localScale = Vector3.one * s;
                instances[i].transform.parent = parent;
                instances[i].name = ((Landmark)i).ToString();
                positionsBuffer[i] = new AccumulatedBuffer(Vector3.zero, 0);
            }

            for (int i = 0; i < lines.Length; ++i)
            {
                lines[i] = Instantiate(linePrefab).GetComponent<LineRenderer>();
                lines[i].transform.parent = parent;
                lines[i].startWidth = s * handLineWidthMultiplier;
                lines[i].endWidth = s * handLineWidthMultiplier;
            }

            if (headPrefab)
            {
                GameObject head = Instantiate(headPrefab);
                head.transform.parent = instances[(int)Landmark.NOSE].transform;
                head.transform.localPosition = headPrefab.transform.position;
                head.transform.localRotation = headPrefab.transform.localRotation;
                head.transform.localScale = headPrefab.transform.localScale;
            }
        }

        public void UpdateLines()
        {
            // Cara (ojos y orejas)
            lines[0].positionCount = 5;
            lines[0].SetPosition(0, Position(Landmark.RIGHT_EAR));
            lines[0].SetPosition(1, Position(Landmark.RIGHT_EYE));
            lines[0].SetPosition(2, Position(Landmark.NOSE));
            lines[0].SetPosition(3, Position(Landmark.LEFT_EYE));
            lines[0].SetPosition(4, Position(Landmark.LEFT_EAR));

            // Boca
            lines[1].positionCount = 2;
            lines[1].SetPosition(0, Position(Landmark.MOUTH_RIGHT));
            lines[1].SetPosition(1, Position(Landmark.MOUTH_LEFT));

            // Torso superior
            lines[2].positionCount = 5;
            lines[2].SetPosition(0, Position(Landmark.RIGHT_SHOULDER));
            lines[2].SetPosition(1, Position(Landmark.RIGHT_ELBOW));
            lines[2].SetPosition(2, Position(Landmark.RIGHT_WRIST));
            lines[2].SetPosition(3, Position(Landmark.RIGHT_THUMB));
            lines[2].SetPosition(4, Position(Landmark.RIGHT_SHOULDER));

            lines[3].positionCount = 5;
            lines[3].SetPosition(0, Position(Landmark.LEFT_SHOULDER));
            lines[3].SetPosition(1, Position(Landmark.LEFT_ELBOW));
            lines[3].SetPosition(2, Position(Landmark.LEFT_WRIST));
            lines[3].SetPosition(3, Position(Landmark.LEFT_THUMB));
            lines[3].SetPosition(4, Position(Landmark.LEFT_SHOULDER));

            // Torso inferior
            lines[4].positionCount = 5;
            lines[4].SetPosition(0, Position(Landmark.RIGHT_HIP));
            lines[4].SetPosition(1, Position(Landmark.RIGHT_KNEE));
            lines[4].SetPosition(2, Position(Landmark.RIGHT_ANKLE));
            lines[4].SetPosition(3, Position(Landmark.RIGHT_FOOT_INDEX));
            lines[4].SetPosition(4, Position(Landmark.RIGHT_HIP));

            lines[5].positionCount = 5;
            lines[5].SetPosition(0, Position(Landmark.LEFT_HIP));
            lines[5].SetPosition(1, Position(Landmark.LEFT_KNEE));
            lines[5].SetPosition(2, Position(Landmark.LEFT_ANKLE));
            lines[5].SetPosition(3, Position(Landmark.LEFT_FOOT_INDEX));
            lines[5].SetPosition(4, Position(Landmark.LEFT_HIP));

            // Hombros a caderas
            lines[6].positionCount = 4;
            lines[6].SetPosition(0, Position(Landmark.RIGHT_SHOULDER));
            lines[6].SetPosition(1, Position(Landmark.RIGHT_HIP));
            lines[6].SetPosition(2, Position(Landmark.LEFT_HIP));
            lines[6].SetPosition(3, Position(Landmark.LEFT_SHOULDER));

            // Piernas
            lines[7].positionCount = 3;
            lines[7].SetPosition(0, Position(Landmark.RIGHT_ANKLE));
            lines[7].SetPosition(1, Position(Landmark.RIGHT_KNEE));
            lines[7].SetPosition(2, Position(Landmark.RIGHT_HIP));

            lines[8].positionCount = 3;
            lines[8].SetPosition(0, Position(Landmark.LEFT_ANKLE));
            lines[8].SetPosition(1, Position(Landmark.LEFT_KNEE));
            lines[8].SetPosition(2, Position(Landmark.LEFT_HIP));

            // Pies
            lines[9].positionCount = 3;
            lines[9].SetPosition(0, Position(Landmark.RIGHT_ANKLE));
            lines[9].SetPosition(1, Position(Landmark.RIGHT_HEEL));
            lines[9].SetPosition(2, Position(Landmark.RIGHT_FOOT_INDEX));

            lines[10].positionCount = 3;
            lines[10].SetPosition(0, Position(Landmark.LEFT_ANKLE));
            lines[10].SetPosition(1, Position(Landmark.LEFT_HEEL));
            lines[10].SetPosition(2, Position(Landmark.LEFT_FOOT_INDEX));
        }

        public Vector3 Direction(Landmark from, Landmark to) =>
            (instances[(int)to].transform.position - instances[(int)from].transform.position).normalized;

        public float Distance(Landmark from, Landmark to) =>
            (instances[(int)from].transform.position - instances[(int)to].transform.position).magnitude;

        public Vector3 LocalPosition(Landmark Mark) =>
            instances[(int)Mark].transform.localPosition;

        public Vector3 Position(Landmark Mark) =>
            instances[(int)Mark].transform.position;
    }
}