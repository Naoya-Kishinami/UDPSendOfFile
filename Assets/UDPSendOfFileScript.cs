using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

public class UDPSendOfFileScript : MonoBehaviour {

	public InputField IPAddressField;
	public InputField IntervalField;
	public Dropdown SendFileSelect;

	private static int PORTNO = 12345;

	private Socket socket;
	private string sendFilePath = "";

	private List<string> sendlst = new List<string>();

	// Use this for initialization
	void Start () {
		Debug.Log ("Socket open");
		socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
		socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.IpTimeToLive, 255);

		sendFilePath = Application.persistentDataPath + @"/test/";
		Debug.Log ("Path : " + sendFilePath);

		if (!Directory.Exists (sendFilePath)) {
			Debug.Log ("Path doesn't exist.");
		}
		else {
			List<string> filelist = new List<string>();
			string[] filePathArray = Directory.GetFiles (sendFilePath, "*.txt", SearchOption.AllDirectories);
			foreach (string filePath in filePathArray) {
				string filename = Path.GetFileName(filePath);
				Debug.Log ("File : " + filename);
				filelist.Add (filename);
			}
			SendFileSelect.ClearOptions ();
			SendFileSelect.AddOptions(filelist);
		}
	}
	
	void OnApplicationQuit() {
		Debug.Log ("Socket close");
		socket.Close ();
	}

	public void SendPush () {
		int interval = int.Parse (IntervalField.text);

		string filepath = sendFilePath + SendFileSelect.options[SendFileSelect.value].text;

		sendlst.Clear ();

		FileInfo fi = new FileInfo(filepath);
		using (StreamReader sr = new StreamReader (fi.OpenRead (), Encoding.UTF8)) {
			while (sr.Peek () >= 0) {
				sendlst.Add (sr.ReadLine ());
			}
		}

		StartCoroutine (Sending(IPAddressField.text, interval));
	}

	IEnumerator Sending(string ipaddress, int interval){
		IPEndPoint remoteIP = new IPEndPoint(IPAddress.Parse(ipaddress), PORTNO);
		Debug.Log ("Send start");
		foreach (string send in sendlst) {
			byte[] data = System.Text.Encoding.UTF8.GetBytes (send);
			socket.SendTo(data, 0, data.Length, SocketFlags.None, remoteIP);

			Debug.Log (send);
			yield return new WaitForSeconds(interval);
		}
		Debug.Log ("Send end");
	}
}
