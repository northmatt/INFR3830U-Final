using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour {
    public float velocity = 3f;
    public TMPro.TMP_Text messageField;
    public TMPro.TMP_InputField inputField;

    private void Start() {
        GameController.instance.player = GetComponent<OnlineSyncController>();
        GameController.instance.entityList = transform.parent;
        GameController.instance.messageField = messageField;
        GameController.instance.inputField = inputField;

        if (inputField) {
            inputField.onSubmit.AddListener((string input) => { GameController.instance.SendServerMessage(input); });
            inputField.DeactivateInputField(true);
        }
    }

    void Update() {
        if (!GameController.instance.gamePaused)
            transform.Translate(new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")).normalized * velocity * Time.deltaTime);
    }
}
