using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour {
    public float moveSpeed = 3f;
    public TMPro.TMP_Text messageField;
    public TMPro.TMP_InputField inputField;

    private void Start() {
        GameController.instance.player = GetComponent<OnlineSyncController>();
        GameController.instance.entityList = transform.parent;
        GameController.instance.messageField = messageField;
        GameController.instance.inputField = inputField;

        inputField.onSubmit.AddListener((string input) => { GameController.instance.SendServerMessage(input); });
    }

    void Update() {
        transform.Translate(Input.GetAxisRaw("Horizontal") * moveSpeed * Time.deltaTime, 0f, Input.GetAxisRaw("Vertical") * moveSpeed * Time.deltaTime);
    }
}
