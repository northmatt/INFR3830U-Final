using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour {
    public float velocity = 3f;
    public TMPro.TMP_Text messageField;
    public TMPro.TMP_InputField messageInputField;
    public TMPro.TMP_InputField nameInputField;
    public Transform playerLobbyInfoPanel;

    private void Start() {
        GameController.instance.player = GetComponent<OnlineSyncController>();
        GameController.instance.entityList = transform.parent;
        GameController.instance.messageField = messageField;
        GameController.instance.messageInputField = messageInputField;
        GameController.instance.nameInputField = nameInputField;
        GameController.instance.playerLobbyInfoPanel = playerLobbyInfoPanel;

        if (messageInputField) {
            messageInputField.onSubmit.AddListener((string input) => { GameController.instance.SendServerMessage(input); });
            messageInputField.DeactivateInputField(true);
        }

        if (nameInputField) {
            nameInputField.onSubmit.AddListener((string input) => { GameController.instance.SetPlayerName(input); });
            nameInputField.DeactivateInputField(true);
        }

        if (!GameController.instance.gamePaused)
            GameController.instance.TogglePauseMenu();
    }

    void Update() {
        if (!GameController.instance.gamePaused)
            transform.Translate(new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")).normalized * velocity * Time.deltaTime);
    }
}
