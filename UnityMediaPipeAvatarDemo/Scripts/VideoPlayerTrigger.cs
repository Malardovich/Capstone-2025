using UnityEngine;
using UnityEngine.Video;

public class VideoPlayerTrigger : MonoBehaviour
{
    public VideoPlayer videoPlayer; // Asigna el VideoPlayer en el inspector
    public string pipeServerObjectName = "PipeServer"; // Nombre del GameObject con PipeServer
    private PipeServer pipeServer;
    private bool hasStarted = false;

    void Start()
    {
        // Busca el PipeServer en la escena
        GameObject pipeObj = GameObject.Find(pipeServerObjectName);
        if (pipeObj != null)
        {
            pipeServer = pipeObj.GetComponent<PipeServer>();
            Debug.Log("PipeServer encontrado y referenciado.");
        }
        else
        {
            Debug.LogWarning("No se encontró un GameObject llamado '" + pipeServerObjectName + "' en la escena.");
        }
    }

    void Update()
    {
        if (pipeServer != null)
            Debug.Log("VideoPlayerTrigger: pipeServer.active = " + pipeServer.active);

        if (!hasStarted && pipeServer != null && pipeServer.active)
        {
            Debug.Log("¡Datos recibidos! Reproduciendo video.");
            videoPlayer.Play();
            hasStarted = true;
        }
    }

    bool pipeServerIsReceivingData()
    {
        // Por defecto, revisa si el avatar está activo (recibiendo datos)
        Debug.Log("PipeServer.active: " + pipeServer.active);
        return pipeServer.active;
    }
}