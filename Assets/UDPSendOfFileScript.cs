using System;
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
	public Dropdown TransFileSelect;

	private static int PORTNO = 12345;

	private Socket socket;
	private string transFilePath = "";

	private List<string> translst = new List<string>();

	// Use this for initialization
	void Start () {
		Debug.Log ("Socket open");
		socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
		socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.IpTimeToLive, 255);
#if UNITY_EDITOR
		transFilePath = "Assets/trans/";
#elif UNITY_ANDROID
		// Path : Android/data/(Package Name)/files/trans
		transFilePath = Application.persistentDataPath + @"/trans/";
#endif
		Debug.Log ("Path : " + transFilePath);

		if (!Directory.Exists (transFilePath)) {
			Debug.Log ("Path doesn't exist.");
		}
		else {
			List<string> filelist = new List<string>();
			string[] filePathArray = Directory.GetFiles (transFilePath, "*.txt", SearchOption.AllDirectories);
			foreach (string filePath in filePathArray) {
				string filename = Path.GetFileName(filePath);
				Debug.Log ("File : " + filename);
				filelist.Add (filename);
			}
			TransFileSelect.ClearOptions ();
			TransFileSelect.AddOptions(filelist);
		}
	}
	
	void OnApplicationQuit() {
		Debug.Log ("Socket close");
		socket.Close ();
	}

	public void TransmitPush () {
		int interval = int.Parse (IntervalField.text);

		string filepath = transFilePath + TransFileSelect.options[TransFileSelect.value].text;

		translst.Clear ();

		FileInfo fi = new FileInfo(filepath);
		using (StreamReader sr = new StreamReader (fi.OpenRead (), Encoding.UTF8)) {
			while (sr.Peek () >= 0) {
				translst.Add (sr.ReadLine ());
			}
		}

		StartCoroutine (Transmission(IPAddressField.text, interval));
	}

	IEnumerator Transmission(string ipaddress, int interval){
		IPEndPoint remoteIP = new IPEndPoint(IPAddress.Parse(ipaddress), PORTNO);

		System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
		stopwatch.Start ();

		Debug.Log ("Transmit start");
		foreach (string trans in translst) {
			byte[] data = System.Text.Encoding.UTF8.GetBytes (trans);
			socket.SendTo(data, 0, data.Length, SocketFlags.None, remoteIP);

			Debug.Log (string.Format("[{0:0.000}] ", (float)stopwatch.Elapsed.TotalSeconds) + trans);
			yield return new WaitForSeconds(interval);
		}
		Debug.Log ("Transmit end");

		stopwatch.Stop ();
	}
}
