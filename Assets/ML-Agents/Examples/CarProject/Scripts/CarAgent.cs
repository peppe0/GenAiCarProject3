using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;



public class CarAgent : Agent
{
    private MSVehicleControllerFree carController;
    private Rigidbody m_AgentRb;

    private Vector3 startingPosition;
    private Quaternion startingRotation;
    private float episodeStartTime;
private float targetLapTime;

    [Header("Checkpoint System")]
    public List<Transform> checkpoints;

    [System.Serializable]
    public class SpawnPoint
    {
        public Transform point;
     public int nextCheckpointIndex; // quale checkpoint deve inseguire chi parte da qui    
    }   

    [Header("Randomized Spawn")]
    public List<SpawnPoint> spawnPoints;
    public float lateralNoise = 1f;      // metri
    public float rotationNoiseDegrees = 8f; // gradi



    [Header("Reward Tuning")]
    public float checkpointReward = 1.0f;
    public float finishReward = 5.0f;
    public float wallPenalty = -1.0f;
    public float timePenalty = -0.001f;       
    public float wrongCheckpointPenalty = -0.5f; 
    public float speedTowardCheckpointFactor = 0.01f; 


    private int nextCheckpointIndex;


    public override void Initialize()
    {
        m_AgentRb = GetComponent<Rigidbody>();
        carController = GetComponent<MSVehicleControllerFree>();
        
        if (m_AgentRb == null)
        Debug.LogError($"❌ MANCA Rigidbody su {gameObject.name}!", this);
    if (carController == null)
        Debug.LogError($"❌ MANCA MSVehicleControllerFree su {gameObject.name}!", this);
    if (checkpoints == null || checkpoints.Count == 0)
        Debug.LogError($"❌ Lista CHECKPOINT vuota su {gameObject.name}!", this);
        // Salviamo la POSIZIONE LOCALE (rispetto al contenitore della pista)
        startingPosition = transform.localPosition;
        startingRotation = transform.localRotation;
    }

   public override void OnEpisodeBegin()
{
    m_AgentRb.velocity = Vector3.zero;
    m_AgentRb.angularVelocity = Vector3.zero;

    if (spawnPoints != null && spawnPoints.Count > 0)
    {
        var chosen = spawnPoints[Random.Range(0, spawnPoints.Count)];

        // Rumore laterale, calcolato nel sistema locale del punto di spawn
        Vector3 lateralOffset = chosen.point.localRotation * Vector3.right * Random.Range(-lateralNoise, lateralNoise);
        Quaternion yawNoise = Quaternion.Euler(0f, Random.Range(-rotationNoiseDegrees, rotationNoiseDegrees), 0f);

        transform.localPosition = chosen.point.localPosition + lateralOffset;
        transform.localRotation = chosen.point.localRotation * yawNoise;

        nextCheckpointIndex = chosen.nextCheckpointIndex;
    }
    else
    {
        // Fallback: comportamento attuale se non configurate spawn points
        transform.localPosition = startingPosition;
        transform.localRotation = startingRotation;
        nextCheckpointIndex = 0;
    }

    if (carController != null)
    {
        carController.verticalInput = 0f;
        carController.horizontalInput = 0f;
    }
}
    
    public override void CollectObservations(VectorSensor sensor)
    {
        // 1. Direzione verso il prossimo checkpoint (vettore locale, 3 float)
        if (checkpoints != null && checkpoints.Count > 0 && nextCheckpointIndex < checkpoints.Count)
        {
            Vector3 dirToCheckpoint = (checkpoints[nextCheckpointIndex].position - transform.position).normalized;
            Vector3 localDir = transform.InverseTransformDirection(dirToCheckpoint);
            sensor.AddObservation(localDir); // 3 valori
        }
        else
        {
            sensor.AddObservation(Vector3.zero); // 3 valori placeholder
        }
        // 2. Velocità locale della macchina (3 float)
        Vector3 localVelocity = transform.InverseTransformDirection(m_AgentRb.velocity);
        sensor.AddObservation(localVelocity); // 3 valori
        // 3. Distanza dal prossimo checkpoint (1 float)
        if (checkpoints != null && checkpoints.Count > 0 && nextCheckpointIndex < checkpoints.Count)
        {
            float dist = Vector3.Distance(transform.position, checkpoints[nextCheckpointIndex].position);
            sensor.AddObservation(dist / 50f); // normalizzato (adatta 50 alla dimensione del circuito)
        }
        else
        {
            sensor.AddObservation(0f);
        }
        // Totale: 7 osservazioni → imposta "Vector Observation Space Size = 7"
    }


    public override void OnActionReceived(ActionBuffers actionBuffers)
    { 
       float accInput = actionBuffers.ContinuousActions[0];
    float steerInput = actionBuffers.ContinuousActions[1];

    if (carController != null)
    {
        carController.verticalInput = accInput;
        carController.horizontalInput = steerInput;
    }
    
    if (checkpoints != null && checkpoints.Count > 0 && nextCheckpointIndex < checkpoints.Count)
    {
        Vector3 dirToCheckpoint = (checkpoints[nextCheckpointIndex].position - transform.position).normalized;
        float velocityTowardCheckpoint = Vector3.Dot(m_AgentRb.velocity, dirToCheckpoint);
        AddReward(speedTowardCheckpointFactor * velocityTowardCheckpoint);
    }
    AddReward(timePenalty);
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
         var continuousActionsOut = actionsOut.ContinuousActions;

        float acc = 0f;
        if (Input.GetKey(KeyCode.W)) acc = 1f;
        else if (Input.GetKey(KeyCode.S)) acc = -1f;
        continuousActionsOut[0] = acc;

        float steer = 0f;
        if (Input.GetKey(KeyCode.D)) steer = 1f;
        else if (Input.GetKey(KeyCode.A)) steer = -1f;
        continuousActionsOut[1] = steer;
    }

    // Aggiungo la gestione delle collisioni con i Checkpoint o i muri
  private void OnTriggerEnter(Collider other)
{
    if (other.CompareTag("finish"))
    {
          bool success = nextCheckpointIndex >= checkpoints.Count;
         float lapTime = Time.time - episodeStartTime;
        // Il traguardo funziona indipendentemente dalla lista checkpoint
        // Opzionale: dare reward solo se ha passato TUTTI i checkpoint

        Academy.Instance.StatsRecorder.Add("Car/Success", success ? 1f : 0f);
        Academy.Instance.StatsRecorder.Add("Car/CheckpointsPassed", nextCheckpointIndex);
        Academy.Instance.StatsRecorder.Add("Car/LapTime", lapTime);

        if (success)
        {
            float timeBonus = Mathf.Clamp(targetLapTime / lapTime, 0.5f, 2f);
            AddReward(finishReward * timeBonus);
            Debug.Log("🏁 Vittoria! Tutti i checkpoint superati + traguardo!");
        }
        else
        {
            // Ha raggiunto il traguardo saltando dei checkpoint
            AddReward(finishReward * 0.5f); // premio ridotto
            Debug.Log($"🏁 Traguardo raggiunto ma mancano {checkpoints.Count - nextCheckpointIndex} checkpoint.");
        }
        EndEpisode();
    }
    else if (other.CompareTag("checkpoint"))
    {
        int triggeredIndex = checkpoints.FindIndex(cp => cp == other.transform);
        
        if (triggeredIndex == nextCheckpointIndex)
        {
            AddReward(checkpointReward);
            Debug.Log($"✅ Checkpoint {nextCheckpointIndex} superato!");
            nextCheckpointIndex++;
        }
        else if (triggeredIndex >= 0 && triggeredIndex < nextCheckpointIndex)
        {
            AddReward(wrongCheckpointPenalty);
            Debug.Log($"⚠️ Checkpoint {triggeredIndex} già superato!");
        }
        else if (triggeredIndex > nextCheckpointIndex)
        {
            AddReward(wrongCheckpointPenalty);
            Debug.Log($"⚠️ Ha saltato i checkpoint da {nextCheckpointIndex} a {triggeredIndex - 1}!");
            nextCheckpointIndex = triggeredIndex + 1;
        }
    }
}
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("wall"))
        {

             Academy.Instance.StatsRecorder.Add("Car/Success", 0f);
             Academy.Instance.StatsRecorder.Add("Car/CheckpointsPassed", nextCheckpointIndex);
             Academy.Instance.StatsRecorder.Add("Car/WallCrash", 1f);

            // Se sbatte contro un muro, diamo una penalità fortissima e terminiamo l'episodio (Ricomincia daccapo).
            AddReward(wallPenalty);
            Debug.Log("Hai sbattuto contro il muro! Episodio terminato.");
            EndEpisode();
        }
    }
}
