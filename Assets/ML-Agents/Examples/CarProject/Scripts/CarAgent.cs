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

    [Header("Reward Tuning")]
    public float checkpointReward = 1.0f;
    public float finishReward = 5.0f;
    public float wallPenalty = -1.0f;
    public float timePenalty = -0.001f;       
    public float wrongCheckpointPenalty = -0.5f; 

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
        // Questo metodo si avvia ogni volta che chiamiamo EndEpisode()
        
        // 1. Fermiamo tutti i vettori e le spinte della macchina
        m_AgentRb.velocity = Vector3.zero;
        m_AgentRb.angularVelocity = Vector3.zero;

        // 2. Resettiamo fisicamente la posizione e rotazione LOCALI
        transform.localPosition = startingPosition;
        transform.localRotation = startingRotation;

        // 3. Fermiamo gli input bloccati dell'acceleratore passati all'asset vettura
        if (carController != null)
        {
            carController.verticalInput = 0f;
            carController.horizontalInput = 0f;
        }
         nextCheckpointIndex = 0;
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
        // Azioni discrete (0 = Nessuna, 1 = Su, 2 = Giù, 3 = Destra, 4 = Sinistra)
        // Ma siccome vogliamo accelerare E sterzare assieme, meglio usare una Branch per il gas (0,1,2)
        // e una Branch per lo sterzo (0,1,2).
        
        // Questo richiede che tu imposti "Discrete Branches" a 2.
        // Branch 0 Size = 3 (0: niente, 1: accelera, 2: frena/indietro)
        // Branch 1 Size = 3 (0: dritto, 1: destra, 2: sinistra)
        
        float accInput = 0f;
        float steerInput = 0f;

        int driveAction = actionBuffers.DiscreteActions[0];
        int steerAction = actionBuffers.DiscreteActions[1];

        // Decodifica Accelerazione
        if (driveAction == 1) accInput = 1f;
        else if (driveAction == 2) accInput = -1f;

        // Decodifica Sterzo
        if (steerAction == 1) steerInput = 1f;
        else if (steerAction == 2) steerInput = -1f;

        // Invia i valori direttamente al MSVehicleControllerFree 
        // DOVRAI AVER MESSO PUBLIC QUELLE VARIABILI NELLO SCRIPT DI MSVehicleSystem!
        if (carController != null)
        {
            carController.verticalInput = accInput;
            carController.horizontalInput = steerInput;
        }
         AddReward(timePenalty);
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        // Leggiamo la tastiera per testare l'auto in Heuristic Only
        var discreteActionsOut = actionsOut.DiscreteActions;
        
        // Asse Verticale (Gas/Freno)
        discreteActionsOut[0] = 0;
        if (Input.GetKey(KeyCode.W)) discreteActionsOut[0] = 1;
        else if (Input.GetKey(KeyCode.S)) discreteActionsOut[0] = 2;

        // Asse Orizzontale (Sterzo)
        discreteActionsOut[1] = 0;
        if (Input.GetKey(KeyCode.D)) discreteActionsOut[1] = 1;
        else if (Input.GetKey(KeyCode.A)) discreteActionsOut[1] = 2;
    }

    // Aggiungo la gestione delle collisioni con i Checkpoint o i muri
  private void OnTriggerEnter(Collider other)
{
    if (other.CompareTag("finish"))
    {
        // Il traguardo funziona indipendentemente dalla lista checkpoint
        // Opzionale: dare reward solo se ha passato TUTTI i checkpoint
        if (nextCheckpointIndex >= checkpoints.Count)
        {
            AddReward(finishReward);
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
    }
}
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("wall"))
        {
            // Se sbatte contro un muro, diamo una penalità fortissima e terminiamo l'episodio (Ricomincia daccapo).
            AddReward(wallPenalty);
            Debug.Log("Hai sbattuto contro il muro! Episodio terminato.");
            EndEpisode();
        }
    }
}
