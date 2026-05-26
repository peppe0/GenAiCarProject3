using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;

public class CarAgent : Agent
{
    private MSVehicleControllerFree carController;
    private Rigidbody m_AgentRb;

    // Aggiungi queste variabili per ripristinare la posizione iniziale
    private Vector3 startingPosition;
    private Quaternion startingRotation;

    public override void Initialize()
    {
        m_AgentRb = GetComponent<Rigidbody>();
        carController = GetComponent<MSVehicleControllerFree>();
        
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

        if (other.CompareTag("checkpoint"))
        {
            // Dai un punteggio positivo all'agente quando passa un checkpoint intermedio!
            AddReward(1.0f);
            Debug.Log("Checkpoint passato! +1");
        }
        else if (other.CompareTag("finish"))
        {
            // Premio enorme per la vittoria!
            AddReward(5.0f);
            Debug.Log("Vittoria! Traguardo raggiunto!");
            EndEpisode();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("wall"))
        {
            // Se sbatte contro un muro, diamo una penalità fortissima e terminiamo l'episodio (Ricomincia daccapo).
            AddReward(-1.0f);
            Debug.Log("Hai sbattuto contro il muro! Episodio terminato.");
            EndEpisode();
        }
    }
}
