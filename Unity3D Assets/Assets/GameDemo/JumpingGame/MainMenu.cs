using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DedicatedServer.Demo.JumpingGame
{
    public class MainMenu : MonoBehaviour
    {
        private TMP_InputField _clientIDInputField;
        private TMP_InputField _serverIPAddressInputField;
        private TMP_InputField _serverPortInputField;
        private Toggle _automaticEntityAuthorityTransferToggle;
        private JumpingGame _gameManager;

        private void Awake()
        {
            this._clientIDInputField = this.transform.Find("ClientIDInputField").GetComponent<TMP_InputField>();
            this._serverIPAddressInputField = this.transform.Find("ServerIPAddressInputField").GetComponent<TMP_InputField>();
            this._serverPortInputField = this.transform.Find("ServerPortInputField").GetComponent<TMP_InputField>();
            this._automaticEntityAuthorityTransferToggle = this.transform.Find("AutomaticEntityAuthorityTransferToggle").GetComponent<Toggle>();
            this._gameManager = GameObject.Find("GameManager").GetComponent<JumpingGame>();
            this._automaticEntityAuthorityTransferToggle.onValueChanged.AddListener(val => this._gameManager.configurations.EnableAutomaticAuthorityTransfer = val);
        }

        // Start is called before the first frame update
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {

        }

        public void StartServerButtonOnClick()
        {
            if (this._clientIDInputField.text == "")
            {
                throw new System.Exception("please input client id");
            }
            ushort serverPort = ushort.Parse(this._serverPortInputField.text);
            this._gameManager.StartServer(serverPort);
            uint clientID = uint.Parse(this._clientIDInputField.text);
            this._gameManager.StartClient(clientID, "127.0.0.1", serverPort);
        }

        public void StartClientButtonOnClick()
        {
            if(this._clientIDInputField.text == "")
            {
                throw new System.Exception("please input client id");
            }
            uint clientID = uint.Parse(this._clientIDInputField.text);
            string serverIPAddress = this._serverIPAddressInputField.text;
            ushort serverPort = ushort.Parse(this._serverPortInputField.text);
            this._gameManager.StartClient(clientID, serverIPAddress, serverPort);
        }
    }
}
