using System.Collections;
using System.Collections.Generic;
using System;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameController : MonoBehaviour {
    public static GameController instance;

    public bool gamePaused = false;
    public bool CursorLocked = false;
    public bool gameQuitting = false;

    public GameObject connectionText;
    public TMPro.TMP_Text messageField;
    public TMPro.TMP_InputField inputField;
    public OnlineSyncController player;
    public GameObject enemyPrefab;
    public Transform entityList;

    //Really need to protect this
    private Queue<byte[]> receiveBufferCommands = new Queue<byte[]>();

    private void OnDisable() {
        gameQuitting = true;
        OnlineSyncController.DisconnectFromServer();
    }

    // Start is called before the first frame update
    void Start() {
        if (instance != null) {
            Destroy(this.gameObject);
            return;
        }

        instance = this;

        CursorHidden(false);
        DontDestroyOnLoad(this.gameObject);
    }

    private void Update() {
        if (SceneManager.GetActiveScene().buildIndex == 1) {
            ExecuteOSCCommands();

            if (Input.GetButtonDown("Cancel") && !gamePaused || Input.GetButtonDown("Fire1") && gamePaused && Input.mousePosition.y > 70f) {
                TogglePauseMenu();
            }
        }
    }

    private void FixedUpdate() {
        if (!OnlineSyncController.ConnectingToServer && OnlineSyncController.ConnectedToServer && SceneManager.GetActiveScene().buildIndex == 0) {
            SceneManager.LoadScene(1);
            CursorHidden(true);
        }

        if (connectionText != null) {
            connectionText.SetActive(OnlineSyncController.ConnectingToServer);
        }
    }

    public void AddCommand(byte[] buffer) {
        receiveBufferCommands.Enqueue(buffer);
    }

    public void SendServerMessage(string message) {
        if (message == "")
            return;

        inputField.text = "";
        OnlineSyncController.SendMessageToServer(player.clientId, message);
    }

    public void SetPlayerName(string name) {
        if (name == "")
            return;

        OnlineSyncController.SetClientName(player.clientId, name);
    }

    private void ExecuteOSCCommands() {
        int executeCount = 0;
        while (receiveBufferCommands.Count > 0 && executeCount < 50) {
            ++executeCount;
            byte[] newCommand = receiveBufferCommands.Dequeue();
            switch ((ClientNetworkCalls)newCommand[0]) {
                case ClientNetworkCalls.TCPClientConnection:
                    if (!player) {
                        Debug.Log("Failed to set Player ID: No Player");
                        AddCommand(newCommand);
                        executeCount = 420;
                        continue;
                    }

                    if (newCommand[2] == 0)
                        player.clientId = newCommand[1];
                    else
                        SetPlayerName("Client " + player.clientId.ToString());

                    break;
                case ClientNetworkCalls.TCPClientDisconnection:
                    foreach (OnlineSyncController curOSC in entityList.GetComponentsInChildren<OnlineSyncController>()) {
                        if (curOSC.typeA == ConnectionType.Recieve && curOSC.clientId == newCommand[1]) {
                            Destroy(curOSC.gameObject);
                            break;
                        }
                    }
                    break;
                case ClientNetworkCalls.TCPClientTransform:
                    Vector3 newPos2 = new Vector3(BitConverter.ToSingle(newCommand, 4), BitConverter.ToSingle(newCommand, 8), BitConverter.ToSingle(newCommand, 12));
                    Instantiate(enemyPrefab, newPos2, Quaternion.identity, entityList).GetComponent<OnlineSyncController>().clientId = newCommand[1];
                    break;
                case ClientNetworkCalls.TCPClientsTransform:
                    int offset1 = 0;
                    Vector3 newPos1 = Vector3.zero;
                    for (int index = 0; index < BitConverter.ToInt32(newCommand, 4); ++index) {
                        offset1 = index * 16;
                        newPos1.Set(BitConverter.ToSingle(newCommand, 12 + offset1), BitConverter.ToSingle(newCommand, 16 + offset1), BitConverter.ToSingle(newCommand, 20 + offset1));
                        Instantiate(enemyPrefab, newPos1, Quaternion.identity, entityList).GetComponent<OnlineSyncController>().clientId = newCommand[8 + offset1];
                    }
                    break;
                case ClientNetworkCalls.TCPSetClientName:
                    foreach (OnlineSyncController curOSC in entityList.GetComponentsInChildren<OnlineSyncController>()) {
                        if (curOSC.clientId == newCommand[1]) {
                            curOSC.clientName = Encoding.ASCII.GetString(newCommand, 4, newCommand.Length - 4);
                            break;
                        }
                    }
                    break;
                case ClientNetworkCalls.TCPClientMessage:
                    foreach (OnlineSyncController curOSC in entityList.GetComponentsInChildren<OnlineSyncController>()) {
                        if (curOSC.clientId == newCommand[1]) {
                            messageField.text += curOSC.clientName + ": " + Encoding.ASCII.GetString(newCommand, 4, newCommand.Length - 4) + "\n";
                            break;
                        }
                    }
                    break;
                case ClientNetworkCalls.UDPClientsTransform:
                    int offset2 = 0;
                    for (int index = 0; index < BitConverter.ToInt32(newCommand, 4); ++index) {
                        offset2 = index * 48;
                        foreach (OnlineSyncController curOSC in entityList.GetComponentsInChildren<OnlineSyncController>()) {
                            if (curOSC.typeA == ConnectionType.Recieve && curOSC.clientId == newCommand[8 + offset2])
                                curOSC.SetTransform(new Vector3(BitConverter.ToSingle(newCommand, 12 + offset2), BitConverter.ToSingle(newCommand, 16 + offset2), BitConverter.ToSingle(newCommand, 20 + offset2)),
                                                    new Vector3(BitConverter.ToSingle(newCommand, 24 + offset2), BitConverter.ToSingle(newCommand, 28 + offset2), BitConverter.ToSingle(newCommand, 32 + offset2)),
                                                    new Vector3(BitConverter.ToSingle(newCommand, 36 + offset2), BitConverter.ToSingle(newCommand, 40 + offset2), BitConverter.ToSingle(newCommand, 44 + offset2)));
                        }
                    }
                    break;
                default:
                    break;
            }
        }

        if (executeCount > 30)
            Debug.Log("High command execute count: " + executeCount);
    }

    public void TogglePauseMenu() {
        SetPause(!gamePaused);

        if (inputField)
            inputField.DeactivateInputField(true);

        //pauseUI.enabled = gamePaused;
    }

    private void SetPause(bool isPaused) {
        gamePaused = isPaused;
        CursorHidden(!gamePaused);

        //Time.timeScale = isPaused ? 0f : 1f;
    }

    public void CursorHidden(bool isHidden) {
        CursorLocked = isHidden;
        Cursor.lockState = CursorLocked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !CursorLocked;
    }

    public static void QuitGame() {
        Application.Quit();
    }

    public void SetupClientConnection(string address) {
        if (OnlineSyncController.ConnectingToServer == true)
            return;

        OnlineSyncController.ConnectToServer(address, 8888);
    }
}
