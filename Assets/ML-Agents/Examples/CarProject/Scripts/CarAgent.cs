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

    [Header("Testing Spawn (Fixed)")]
    [Tooltip("Se attivo, disabilita la scelta casuale e il rumore: la macchina parte SEMPRE dallo spawn point indicato in 'Fixed Spawn Index'. Usare per il testing del circuito.")]
    public bool useFixedSpawn = false;
    [Tooltip("Indice dello spawn point (nella lista 'Spawn Points') da usare quando 'Use Fixed Spawn' è attivo.")]
    public int fixedSpawnIndex = 0;



    [Header("Reward Tuning")]
    public float checkpointReward = 1.0f;
    public float finishReward = 5.0f;
    public float wallPenalty = -1.0f;
    public float timePenalty = -0.001f;       
    public float wrongCheckpointPenalty = -0.5f;
    public float speedTowardCheckpointFactor = 0.01f;
    [Tooltip("Tempo di riferimento (in secondi) per un giro 'decente'. Un lap più veloce di questo dà bonus (>1x), più lento dà malus (<1x), con clamp a 0.5x-2x.")]
    public float targetLapTime = 25f;


    private int nextCheckpointIndex;

    // Stato per rilevare il TIMEOUT: episodio scaduto per MaxStep senza traguardo
    // né schianto. Senza questo, quegli episodi non verrebbero contati nelle metriche.
    private bool episodeResolved = false;
    private bool isFirstEpisode = true;

    // Cronometraggio "giro lanciato": il tempo parte quando la macchina incontra
    // il PRIMO checkpoint (che diventa il riferimento di quel giro) e si chiude
    // quando ci ritorna dopo aver percorso tutti gli altri in ordine.
    private bool lapStarted = false;
    private float lapStartTime;
    private int referenceIndex = -1; // checkpoint di riferimento, scelto a runtime
    private int checkpointsPassedThisEpisode = 0; // conteggio reale (per la metrica)


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

        if (spawnPoints != null)
        {
            for (int i = spawnPoints.Count - 1; i >= 0; i--)
            {
                if (spawnPoints[i].point == null)
                {
                    Debug.LogError($"❌ SpawnPoint[{i}] su {gameObject.name} non ha un Transform assegnato in 'Point'! Rimosso dalla lista.", this);
                    spawnPoints.RemoveAt(i);
                }
            }
        }
        if (checkpoints != null)
        {
            for (int i = 0; i < checkpoints.Count; i++)
            {
                if (checkpoints[i] == null)
                    Debug.LogError($"❌ Checkpoints[{i}] su {gameObject.name} non ha un Transform assegnato!", this);
            }
        }
        // Salviamo la POSIZIONE LOCALE (rispetto al contenitore della pista)
        startingPosition = transform.localPosition;
        startingRotation = transform.localRotation;

        // Evita episodi infiniti: se l'auto si blocca (ribaltata, incastrata in
        // salita, caduta), l'episodio scade dopo MaxStep step invece di congelare
        // il training. Se hai già impostato MaxStep nell'Inspector, viene rispettato.
        if (MaxStep == 0) MaxStep = 3000;
    }

   public override void OnEpisodeBegin()
{
    // Se l'episodio PRECEDENTE non è stato risolto (né traguardo né muro), è
    // scaduto per MaxStep: lo registriamo come timeout, altrimenti sparirebbe
    // dalle metriche e gonfierebbe il success rate.
    if (!isFirstEpisode && !episodeResolved)
    {
        LogEpisodeStats(false, false, true);
    }
    isFirstEpisode = false;
    episodeResolved = false;

    m_AgentRb.velocity = Vector3.zero;
    m_AgentRb.angularVelocity = Vector3.zero;

    if (spawnPoints != null && spawnPoints.Count > 0)
    {
        SpawnPoint chosen;
        Vector3 lateralOffset;
        Quaternion yawNoise;

        if (useFixedSpawn)
        {
            // Modalità TESTING: spawn fisso, nessuna casualità, nessun rumore.
            int index = Mathf.Clamp(fixedSpawnIndex, 0, spawnPoints.Count - 1);
            if (index != fixedSpawnIndex)
                Debug.LogWarning($"⚠️ Fixed Spawn Index {fixedSpawnIndex} fuori range su {gameObject.name}. Uso l'indice {index}.", this);
            chosen = spawnPoints[index];
            lateralOffset = Vector3.zero;
            yawNoise = Quaternion.identity;
        }
        else
        {
            // Modalità TRAINING: spawn casuale con rumore.
            chosen = spawnPoints[Random.Range(0, spawnPoints.Count)];
            lateralOffset = chosen.point.localRotation * Vector3.right * Random.Range(-lateralNoise, lateralNoise);
            yawNoise = Quaternion.Euler(0f, Random.Range(-rotationNoiseDegrees, rotationNoiseDegrees), 0f);
        }

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

    // Nuovo episodio: il giro cronometrato non è ancora iniziato. Partirà quando
    // la macchina incontrerà il primo checkpoint (che diventa il riferimento).
    lapStarted = false;
    referenceIndex = -1;
    checkpointsPassedThisEpisode = 0;
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
    
    if (checkpoints != null && checkpoints.Count > 0)
    {
        // Reward denso: premia l'avvicinarsi al prossimo checkpoint atteso.
        // Quando il giro sta per chiudersi, il prossimo atteso è il checkpoint di
        // riferimento, quindi questo guida naturalmente anche il tratto finale.
        int idx = nextCheckpointIndex % checkpoints.Count;
        Vector3 dirToCheckpoint = (checkpoints[idx].position - transform.position).normalized;
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
    // Guardia: servono almeno 2 checkpoint (il riferimento + un intermedio).
    if (checkpoints == null || checkpoints.Count == 0) return;
    if (!other.CompareTag("checkpoint")) return;

    int triggeredIndex = checkpoints.FindIndex(cp => cp == other.transform);
    if (triggeredIndex < 0) return; // non è uno dei nostri checkpoint

    // ---- PRIMO checkpoint incontrato: diventa il RIFERIMENTO e avvia il giro ----
    if (!lapStarted)
    {
        referenceIndex = triggeredIndex;
        lapStarted = true;
        lapStartTime = Time.time;
        nextCheckpointIndex = (triggeredIndex + 1) % checkpoints.Count;
        checkpointsPassedThisEpisode = 1; // il riferimento è il primo checkpoint superato
        AddReward(checkpointReward);
        Debug.Log($"🟢 Giro AVVIATO (riferimento = checkpoint {referenceIndex}).");
        return;
    }

    // ---- Giro in corso: i checkpoint vanno presi nell'ordine atteso ----
    if (triggeredIndex == nextCheckpointIndex)
    {
        if (triggeredIndex == referenceIndex)
        {
            // Tornato al riferimento dopo aver percorso TUTTI gli altri in ordine
            // → GIRO COMPLETO. Tempo pulito (loop intero), indipendente dallo spawn.
            float lapTime = Time.time - lapStartTime;
            float timeBonus = Mathf.Clamp(targetLapTime / lapTime, 0.5f, 2f);
            AddReward(finishReward * timeBonus);

            LogEpisodeStats(true, false, false);
            Academy.Instance.StatsRecorder.Add("Car/LapTime", lapTime);
            Debug.Log($"🏁 Giro COMPLETATO in {lapTime:F2}s (bonus x{timeBonus:F2})!");

            episodeResolved = true;
            EndEpisode();
        }
        else
        {
            AddReward(checkpointReward);
            checkpointsPassedThisEpisode++;
            Debug.Log($"✅ Checkpoint {triggeredIndex} superato!");
            nextCheckpointIndex = (nextCheckpointIndex + 1) % checkpoints.Count;
        }
    }
    else
    {
        // Ordine sbagliato (saltato o già passato): penalità, indice invariato.
        // Impedisce il reward hacking da scorciatoia: non si avanza saltando.
        AddReward(wrongCheckpointPenalty);
        Debug.Log($"⚠️ Checkpoint {triggeredIndex} fuori sequenza (atteso {nextCheckpointIndex}). Non conta.");
    }
}
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("wall"))
        {
            // Registra l'esito: schianto contro il muro.
            LogEpisodeStats(false, true, false);

            // Penalità e fine episodio.
            AddReward(wallPenalty);
            Debug.Log("Hai sbattuto contro il muro! Episodio terminato.");
            episodeResolved = true;
            EndEpisode();
        }
    }

    // Registra le metriche di fine episodio come TASSI (0/1): così la media su
    // TensorBoard è direttamente la percentuale di successi/schianti/timeout.
    // Va chiamato una volta per OGNI episodio concluso, in qualunque modo finisca.
    private void LogEpisodeStats(bool success, bool crash, bool timeout)
    {
        var stats = Academy.Instance.StatsRecorder;
        stats.Add("Car/Success", success ? 1f : 0f);
        stats.Add("Car/WallCrash", crash ? 1f : 0f);
        stats.Add("Car/Timeout", timeout ? 1f : 0f);
        stats.Add("Car/CheckpointsPassed", checkpointsPassedThisEpisode);
    }
}
