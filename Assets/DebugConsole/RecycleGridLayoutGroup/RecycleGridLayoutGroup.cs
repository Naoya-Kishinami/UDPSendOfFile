using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;

/// <summary>
/// スクロール中に画面外に出たオブジェクトを使いまわし、
/// パフォーマンスを落とすこと無く大量のリストを扱えるGridLayoutGroupコンポーネント
/// </summary>
public class RecycleGridLayoutGroup : MonoBehaviour
{
	//****************************************************************
	#region Variables

	/// リスト内に並べるリストアイテムプレハブ
	[SerializeField] private GameObject _listItemPrefab = null;
	/// リストアイテムのサイズ
	[SerializeField] private Vector2 _cellSize = new Vector2( 32, 32 );
	/// 並べる時の空白
	[SerializeField] private Vector2 _cellSpacing = new Vector2( 0, 0 );
	/// リストアイテムの総数
	[SerializeField] private int _listItemCount = 0;
	/// 
	[SerializeField] private int _bufferLength = 1;

	/// 表示中のオブジェクト辞書。keyはリストのindex番号
	private Dictionary<int,ListItemContent> _showObjectDic = new Dictionary<int, ListItemContent>();
	/// 非表示のオブジェクトリスト。識別の必要はないのでこちらは単なる配列。
	private List<ListItemContent> _hideObjects = new List<ListItemContent>();

	/// 非表示にしたオブジェクトを退避させるオブジェクト
	private Transform _recycleBoxObject = null;
	/// 1列に表示できる数
	private int _lineCount = 1;
	/// 初期化フラグ
	private bool _isInit = false;

	/// リストアイテムが画面内に入った直後のイベント
	private Action<ListItemContent,Action> _onVisibleListContent;
	/// リストアイテムが画面外に出ていった直後のイベント
	private Action<ListItemContent> _onHideListContent;

	/// 定期的な非表示オブジェクトの掃除コルーチン
	private Coroutine _cleaningCoroutine = null;

	#endregion


	//****************************************************************
	#region Propaties

	/// rectTransformのアクセスを軽くする小さな足掻き
	private RectTransform _rectTransform = null;
	private RectTransform rectTransform {
		get {
			if( _rectTransform == null ) _rectTransform = this.transform as RectTransform;
			return _rectTransform;
		}
	}

	/// リストアイテムの総数
	public int listItemCount {
		get { return _listItemCount; }
		set { _listItemCount = value; CalcScrollArea(); }
	}

	/// リサイクルするためのオブジェクトを余分に何列分残しておくか( 1 = 1列 )
	public int bufferLength {
		get { return _bufferLength; }
		set { _bufferLength = value; }
	}

	/// リストアイテムが画面内に入った直後のイベント
	public Action<ListItemContent,Action> onVisibleListItem {
		get { return _onVisibleListContent; }
		set { _onVisibleListContent = value; }
	}

	/// リストアイテムが画面外に出ていった直後のイベント
	public Action<ListItemContent> onHideListItem {
		get { return _onHideListContent; }
		set { _onHideListContent = value; }
	}

	/// 0以下だと除算で死ぬので……
	private int lineCount {
		get { return _lineCount; }
		set { _lineCount = (value <= 0) ? 1 : value; }
	}

	/// リストアイテムのサイズ
	public Vector2 cellSize {
		get { return _cellSize; }
		set { _cellSize = value; }
	}

	/// 並べる時の空白
	public Vector2 cellSpacing {
		get { return _cellSpacing; }
		set { _cellSpacing = value; }
	}

	#endregion


	//****************************************************************
	#region MonoBehaviour LifeCycle

	/// <summary>
	/// Start はスクリプトが有効で、Update メソッドが最初に呼び出される前のフレームで呼び出されます
	/// </summary>
	void Start() {
		StartCoroutine( InitScrollArea() );
	}

	/// <summary>
	/// この関数はオブジェクトが有効/アクティブになったときに呼び出されます
	/// </summary>
	void OnEnable() {
		StartCleaningCoroutine();
	}

	/// <summary>
	/// この関数は Behaviour が無効/非アクティブになったときに呼び出されます
	/// </summary>
	void OnDisable() {
		StopCleaningCoroutine();
	}

	#endregion


	//****************************************************************
	#region スクロールエリアを計算

	/// <summary>
	/// スクロールエリアを計算
	/// </summary>
	private void CalcScrollArea()
	{
		Vector2 listItemInterval = new Vector2( cellSize.x + cellSpacing.x, cellSize.y + cellSpacing.y );

		Vector2 scrollArea = new Vector2( 0, listItemInterval.y );
		if( (_listItemCount % lineCount) > 0 ) {
			scrollArea.y *= ((_listItemCount / lineCount) + 1);
		}
		else {
			scrollArea.y *= (_listItemCount / lineCount);
		}

		rectTransform.sizeDelta = scrollArea;

		OnMoveScrollEvent( Vector2.zero );
	}

	/// <summary>
	/// スクロール領域を初期化する
	/// </summary>
	private IEnumerator InitScrollArea()
	{
		_isInit = false;

		// 画面外に出たオブジェクトを一旦退避する場所
		if( _recycleBoxObject == null )
		{
			GameObject go = new GameObject();
			go.name = "RecycleContent";
			_recycleBoxObject = go.transform;
			_recycleBoxObject.SetParent( rectTransform.parent );
			_recycleBoxObject.localPosition = Vector3.zero;
			_recycleBoxObject.localScale = Vector3.one;
		}

		// Scroll
		ScrollRect scrollRect = rectTransform.GetComponentInParent<ScrollRect>();
		if( scrollRect != null ) {
			scrollRect.onValueChanged.AddListener( OnMoveScrollEvent );
		}

		// 各種リストをクリア
		_showObjectDic = new Dictionary<int, ListItemContent>();
		_hideObjects = new List<ListItemContent>();
		foreach( Transform tf in this.transform ) {
			Destroy( tf.gameObject );
		}
		foreach( Transform tf in _recycleBoxObject ) {
			Destroy( tf.gameObject );
		}

		// Updateを通る前(Startの直後だと)
		// 何故かrectの幅がうまく取れない
		yield return null;

		Vector2 listItemInterval = new Vector2( cellSize.x + cellSpacing.x, cellSize.y + cellSpacing.y );
		RectTransform rt = this.transform.parent as RectTransform;

		lineCount = (int)( rt.rect.width / listItemInterval.x );

		// スクロールエリアを計算
		Vector2 scrollArea = new Vector2( 0, listItemInterval.y );
		if( (_listItemCount % lineCount) > 0 ) {
			scrollArea.y *= ((_listItemCount / lineCount) + 1);
		}
		else {
			scrollArea.y *= (_listItemCount / lineCount);
		}
		rectTransform.sizeDelta = scrollArea;

		_isInit = true;

		yield return null;

		CalcScrollArea();
	}

	#endregion


	//****************************************************************
	#region スクロールイベント中の処理

	/// <summary>
	/// スクロールした時のイベント
	/// </summary>
	/// <param name="scrollPos">Scroll position.</param>
	public void OnMoveScrollEvent( Vector2 scrollPos )
	{
		if( _isInit == false || _listItemCount == 0 ) return;

		Transform thisTf = this.transform;
		RectTransform viewRt = thisTf.parent.GetComponent<RectTransform>();
		float viewTop = rectTransform.localPosition.y * -1;
		float viewBottom = viewTop - viewRt.rect.height;

		// 画面外のリストアイテムを非表示
		HideObjectsOutsideView( viewTop, viewBottom );

		// 画面内に入っているリストアイテムを表示
		ShowObjectsInsideView( viewTop, viewBottom );
	}

	/// <summary>
	/// 画面外のリストアイテムを非表示にする
	/// </summary>
	/// <param name="viewTop">View top.</param>
	/// <param name="viewBottom">View bottom.</param>
	private void HideObjectsOutsideView( float viewTop, float viewBottom )
	{
		foreach( Transform child in rectTransform )
		{
			if( child.gameObject.activeSelf == false ) continue;

			RectTransform rt = child.GetComponent<RectTransform>();

			float itemTop = rt.localPosition.y;
			float itemBottom = itemTop - cellSize.y - cellSpacing.y;


			if( itemBottom > viewTop || itemTop < viewBottom ) {
				HideListItem( child.GetComponent<ListItemContent>() );
			}
		}
	}

	/// <summary>
	/// 画面内に入っているリストアイテムを表示状態にする
	/// </summary>
	/// <param name="viewTop">View top.</param>
	/// <param name="viewBottom">View bottom.</param>
	private void ShowObjectsInsideView( float viewTop, float viewBottom )
	{
		int i = (int)(Mathf.Abs( viewTop ) / (cellSize.y + cellSpacing.y)) * lineCount;
		i--;

		while( true )
		{
			i++;
			if( i >= _listItemCount ) break;
			float top = GetRowsPosByIndex( i );
			if( top < viewBottom ) break;
			if( _showObjectDic.ContainsKey( i ) ) { continue; }

			RecycleListItem( i );
		}
	}

	#endregion

	#region 全更新

	/// <summary>
	/// 全てのリストを更新する
	/// (実際は見えてるリストだけ更新される)
	/// </summary>
	public void UpdateListAll()
	{
		List<ListItemContent> showObj = new List<ListItemContent>();
		foreach( ListItemContent item in _showObjectDic.Values ) {
			showObj.Add( item );
		}

		// まず全て非表示状態にする
		foreach( ListItemContent item in showObj ) {
			HideListItem( item );
		}

		// 見えてるアイテムを更新
		if( _isInit == false || _listItemCount == 0 ) return;

		RectTransform viewRt = this.transform.parent as RectTransform;
		float viewTop = rectTransform.localPosition.y * -1;
		float viewBottom = viewTop - viewRt.rect.height;

		ShowObjectsInsideView( viewTop, viewBottom );
	}

	#endregion

	//****************************************************************
	#region リストアイテム 表示/非表示

	/// <summary>
	/// 対象のリストアイテムを非表示にする
	/// </summary>
	/// <param name="listItem">List item.</param>
	private void HideListItem( ListItemContent listItem )
	{
		_showObjectDic.Remove( listItem.index );
		_hideObjects.Add( listItem );
		listItem.gameObject.SetActive( false );
		listItem.transform.SetParent( _recycleBoxObject );
		listItem.transform.localPosition = Vector3.zero;
		listItem.isHide = true;

		if( _onHideListContent != null ) _onHideListContent( listItem );
	}

	/// <summary>
	/// リサイクル可能な非表示オブジェクトがあればリサイクルし、
	/// なければ新たに生成する。
	/// </summary>
	/// <returns>The list item.</returns>
	/// <param name="index">Index.</param>
	private ListItemContent RecycleListItem( int index )
	{
		if( index >= _listItemCount ) return null;

		GameObject obj;
		ListItemContent listItem;
		RectTransform rt;

		// 既に表示されている状態
		if( _showObjectDic != null && _showObjectDic.ContainsKey( index ) ) {
			return _showObjectDic[index];
		}
		// まだ表示状態になっていない場合
		else
		{
			listItem = null;

			// リサイクル可能なオブジェクトを検索
			foreach( ListItemContent hideObj in _hideObjects ) {
				if( hideObj.state == ListItemContent.eState.Ready ) {
					listItem = hideObj;
					break;
				}
			}

			// リサイクル可能なオブジェクトがある場合は使いまわす
			if( listItem != null ) {
				obj = listItem.gameObject;
				obj.SetActive( true );
				obj.transform.SetParent( this.transform );
				listItem = obj.GetComponent<ListItemContent>();
				_hideObjects.Remove( listItem );
				_showObjectDic.Add( index, listItem );
			}
			// リサイクル可能なオブジェクトがない場合は新規生成
			else {
				obj = Instantiate( _listItemPrefab );
				obj.transform.SetParent( this.transform );
				var rtf = obj.transform as RectTransform;
				rtf.pivot = new Vector2( 0.0f, 1.0f );
				listItem = obj.AddComponent<ListItemContent>();
				_showObjectDic.Add( index, listItem );
			}
		}

		// index番号から座標を算出
		float x = GetColsPosByIndex( index );
		float y = GetRowsPosByIndex( index );

		// リストアイテムの設定
		obj.name = index.ToString();
		listItem.index = index;
		listItem.isHide = false;
		listItem.state = ListItemContent.eState.Loading;
		// 位置・サイズを修正
		rt = obj.GetComponent<RectTransform>();
		rt.localPosition = new Vector3( x, y, 0.0f );
		rt.localScale = Vector3.one;
		rt.sizeDelta = cellSize;

		// 表示状態になった旨をコールバックして通知
		if( _onVisibleListContent != null ) {
			_onVisibleListContent( listItem, () => {
				listItem.state = ListItemContent.eState.LoadCompleted;
			});
		}
		else {
			listItem.state = ListItemContent.eState.LoadCompleted;
		}

		return listItem;
	}
	
	/// <summary>
	/// 削除する
	/// </summary>
	/// <param name="listItem"></param>
	private void DestroyListItem( ListItemContent listItem )
	{
		if( _hideObjects.Count <= (bufferLength*lineCount) ) return;

		if( _showObjectDic.ContainsKey( listItem.index ) ) {
			_showObjectDic.Remove(listItem.index);
		}
		_hideObjects.Remove( listItem );
		GameObject.Destroy( listItem.gameObject );
	}

	#endregion


	//****************************************************************
	#region クリーニングコルーチン

	/// <summary>
	/// 無駄なオブジェクトを削除するコルーチンを開始
	/// </summary>
	public void StartCleaningCoroutine()
	{
		if( _cleaningCoroutine != null ) StopCoroutine( _cleaningCoroutine );
		_cleaningCoroutine = StartCoroutine( ClockSignal( 1.0f, () => {
			OnCleaning( 0.1f );
		}));
	}

	/// <summary>
	/// 無駄なオブジェクトを削除するコルーチンを停止
	/// </summary>
	public void StopCleaningCoroutine()
	{
		if( _cleaningCoroutine != null ) StopCoroutine( _cleaningCoroutine );
		_cleaningCoroutine = null;
	}

	/// <summary>
	/// 無駄なオブジェクトを削除するコルーチン
	/// </summary>
	/// <param name="useCpu">CPU使用率(1.0f=100%)</param>
	public void OnCleaning( float useCpu )
	{
		if( _hideObjects.Count <= (bufferLength*lineCount) ) return;

		float startTime = Time.realtimeSinceStartup;
		float limitTime = startTime + (useCpu / 60.0f);
		
		int len = _hideObjects.Count;
		for( int i = len - 1; i >= 0; --i )
		{
			if( _hideObjects.Count <= (lineCount * bufferLength) ) break;
			if( Time.realtimeSinceStartup >= limitTime ) break;
			
			ListItemContent listItem = _hideObjects[i];
			if( listItem.state != ListItemContent.eState.Ready ) continue;

			_hideObjects.Remove( listItem );
			GameObject.Destroy( listItem.gameObject );
		}
	}

	/// <summary>
	/// 一定時間立ったら定期的にコールバックを返すコルーチン
	/// </summary>
	/// <returns>The signal.</returns>
	/// <param name="interval">Interval.</param>
	/// <param name="callback">Callback.</param>
	public IEnumerator ClockSignal( float interval, Action callback )
	{
		while( true )
		{
			yield return new WaitForSeconds( interval );
			if( callback != null ) callback();
		}
	}

	#endregion


	//****************************************************************
	#region etc

	private float GetColsPosByIndex( int index ) {
		return (cellSize.x + cellSpacing.x) * (index % lineCount);
	}
	private float GetRowsPosByIndex( int index ) {
		return (cellSize.y + cellSpacing.y) * (index / lineCount) * -1;
	}

	#endregion
}
