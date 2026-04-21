using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.UI;

namespace MobileConsole.UI
{
    public interface IRecycleScrollViewDelegate
    {
        ScrollViewCell ScrollCellCreated(int cellIndex);
        void ScrollCellSelected(int cellIndex);
		void ScrollCellWillDisplay(ScrollViewCell cell);
        float ScrollCellSize(int cellIndex);
        int ScrollCellCount();
    }

	public class RecycleScrollView : MonoBehaviour
	{
		///////////////////////////////////////////
		// Scroll Orientation
		///////////////////////////////////////////
		protected enum ScrollOrientation
		{
			Horizontal,
			Vertical
		}

        protected enum ReloadingState
        {
            Idle,
            Reloading,
            ReloadingSuspend
        }


        ///////////////////////////////////////////
        // Class Variable
        ///////////////////////////////////////////
        [SerializeField]
		protected ScrollRect _scrollRect;

		[SerializeField]
		protected ScrollRectWorkaround _scrollRectWorkAround;

		[SerializeField]
		protected RectTransform _viewPort;

		[SerializeField]
		protected RectTransform _viewContent;

		[SerializeField]
		protected ScrollOrientation _scrollOrientation;

		[SerializeField]
		protected float _spacing;

        [SerializeField]
        protected float _headerSpace;

        [SerializeField]
		protected float _footerSpace;

		[SerializeField]
		protected List<ScrollViewCell> _cellTemplates;
		Dictionary<string, List<ScrollViewCell>> _cellPools = new Dictionary<string, List<ScrollViewCell>>();

		IRecycleScrollViewDelegate _delegate;

		protected List<ScrollViewCell> _activeCells = new List<ScrollViewCell>();

		protected List<ScrollViewCellInfo> _cellInfos = new List<ScrollViewCellInfo>();
		protected Pool<ScrollViewCellInfo> _poolCellInfo = new Pool<ScrollViewCellInfo>();

		Vector2 _lastScrollPos;
		Vector2 _contentOffsetMax;
		float _viewportHeight;
		float _lastViewPortHeight;
		bool _isDestroyed;
	        ReloadingState _reloadingState = ReloadingState.Idle;

	        public Vector2 ScrollPosition
		{
			get
			{
				if (_scrollRect == null)
					return Vector2.zero;

				return _scrollRect.normalizedPosition;
			}
			set 
			{
				if (!CanAccessView())
					return;

				_scrollRect.normalizedPosition = value;
				OnScrollRectChanged(Vector2.zero);
			}
		}

		void Awake()
		{
			if (!CanAccessView())
				return;

			_lastScrollPos = _viewContent.localPosition;
			_scrollRect.onValueChanged.AddListener(OnScrollRectChanged);
		}

		void OnDestroy()
		{
			_isDestroyed = true;
			StopAllCoroutines();

			if (_scrollRect != null)
			{
				_scrollRect.onValueChanged.RemoveListener(OnScrollRectChanged);
			}

			_activeCells.Clear();
			_cellPools.Clear();
		}

		void OnEnable()
		{
			if (!CanAccessView())
				return;

			_contentOffsetMax = _viewContent.offsetMax;
			_viewportHeight = _viewPort.rect.height;

            if (_reloadingState == ReloadingState.Idle)
            {
                DeactiveAllCell();
                UpdateCellVisibility();
            }
            else if (_reloadingState == ReloadingState.ReloadingSuspend)
            {
                ReloadData();
            }
        }

		void Update()
		{
			if (!CanAccessView())
				return;

			_viewportHeight = _viewPort.rect.height;
			if (_viewportHeight - _lastViewPortHeight > float.Epsilon)
			{
				OnScrollRectMoveUp();
				_lastViewPortHeight = _viewportHeight;
			}
			else if (_viewportHeight - _lastViewPortHeight < -float.Epsilon)
			{
				_lastViewPortHeight = _viewportHeight;
				OnScrollRectMoveDown();
			}
		}

		void OnScrollRectChanged(Vector2 position)
		{
			if (!CanAccessView())
				return;

			if (_lastScrollPos.y > _viewContent.localPosition.y)
			{
				OnScrollRectMoveDown();
			}
			else if (_lastScrollPos.y < _viewContent.localPosition.y)
			{
				OnScrollRectMoveUp();
			}
			_lastScrollPos = _viewContent.localPosition;
		}

		public void SetDelegate(IRecycleScrollViewDelegate _delegate)
		{
			this._delegate = _delegate;

			if (_cellTemplates == null)
				return;

			foreach (var cell in _cellTemplates)
			{
				if (cell == null || string.IsNullOrEmpty(cell.identifier))
				{
					Debug.LogError("One or many cell templates is null. Please check again!");
					continue;
				}

				_cellPools[cell.identifier] = new List<ScrollViewCell>();
			}
		}

		public void AddCellTemplates(List<ScrollViewCell> templates)
		{
			if (templates != null)
			{
				if (_cellTemplates == null)
				{
					_cellTemplates = new List<ScrollViewCell>();
				}

				_cellTemplates.AddRange(templates);
			}
		}

		public void AddCellTemplate(ScrollViewCell template)
		{
			if (template != null)
			{
				if (_cellTemplates == null)
				{
					_cellTemplates = new List<ScrollViewCell>();
				}

				_cellTemplates.Add(template);
			}
		}

	        public void ReloadData()
	        {
	            if (_isDestroyed)
	                return;

	            if (isActiveAndEnabled)
	            {
                StartCoroutine(_ReloadData());
            }
            else
            {
                _reloadingState = ReloadingState.ReloadingSuspend;
            }
        }

	        IEnumerator _ReloadData()
	        {
	            if (_delegate == null)
            {
                throw new Exception("You must set delegate first");
            }

            if (_reloadingState == ReloadingState.Reloading)
                yield break;
            _reloadingState = ReloadingState.Reloading;

	            while (true)
	            {
	                if (!CanAccessView())
	                {
	                    _reloadingState = ReloadingState.Idle;
	                    yield break;
	                }

	                LazyGetViewportHeight();
	                if (_viewportHeight <= 0)
                    yield return new WaitForEndOfFrame();
                else break;
            }

            RecreateCellInfo();
            CalculateCellPosition();
            DeactiveAllCell();
            UpdateViewContent();
            UpdateCellVisibility();

	            if (_scrollRectWorkAround != null && _scrollRect != null)
	                _scrollRectWorkAround.ForceUpdateScrollBar(_scrollRect.normalizedPosition);

            _reloadingState = ReloadingState.Idle;
        }

	        public void AddCell(bool isFirst = false)
		{
			if (_delegate == null || !CanAccessView())
				return;

			ScrollViewCellInfo cellInfo = _poolCellInfo.Get();

			LazyGetViewportHeight();
			if (isFirst)
			{
				cellInfo.index = 0;
				cellInfo.position = 0;
				cellInfo.size = _delegate.ScrollCellSize(cellInfo.index);

				CalculateCellPosition();
				DeactiveAllCell();
				UpdateCellVisibility();
			}
			else
			{
				cellInfo.index = _cellInfos.Count;
				cellInfo.size = _delegate.ScrollCellSize(cellInfo.index);

				if (_cellInfos.Count > 0)
				{
					ScrollViewCellInfo lastCellInfo = _cellInfos[_cellInfos.Count - 1];
					cellInfo.position = lastCellInfo.position + lastCellInfo.size + _spacing;
				}
				else
				{
					cellInfo.position = _headerSpace;
				}
			}

			_cellInfos.Add(cellInfo);
			float newViewContentSize = UpdateViewContent();


			// If viewContentSize > _viewportheight, OnScrollMoveDown will be called automatically
			if (gameObject.activeInHierarchy && newViewContentSize < _viewportHeight)
			{
				if (IsCellInsideViewPort(cellInfo))
				{
					AddActiveCell(cellInfo, isFirst);
				}
			}
		}

		public ReadOnlyCollection<ScrollViewCell> GetActiveCells()
		{
			return _activeCells.AsReadOnly();
		}

		public ScrollViewCell GetActiveCellAtIndex(int cellIndex)
		{
			return _activeCells.Find(cell => cell != null && cell.info != null && cell.info.index == cellIndex);
		}

		protected virtual void OnScrollRectMoveUp()
		{
			if (!CanAccessView())
				return;

			RemoveMissingActiveCells();

			// Cached data for further optimization
			_contentOffsetMax = _viewContent.offsetMax;

			int lastCellIndex = _activeCells.Count > 0 ? _activeCells[_activeCells.Count - 1].info.index : 0;
			List<ScrollViewCell> removedCells = _activeCells.FindAll(cell => cell != null && cell.info != null && IsCellInsideViewPort(cell.info) == false);
			foreach (var cell in removedCells)
			{
				DeactiveCell(cell);
			}

			float endViewPosition = _contentOffsetMax.y + _viewportHeight;
			for (int i = lastCellIndex + 1; i < _cellInfos.Count; i++)
			{
				if (_cellInfos[i].position > endViewPosition)
				{
					break;
				}

				if (IsCellInsideViewPort(_cellInfos[i]))
				{
					AddActiveCell(_cellInfos[i]);
				}
			}
		}

		protected virtual void OnScrollRectMoveDown()
		{
			if (!CanAccessView())
				return;

			RemoveMissingActiveCells();

			// Cached data for further optimization
			_contentOffsetMax = _viewContent.offsetMax;

			int firstCellIndex = _activeCells.Count > 0 ? _activeCells[0].info.index : _cellInfos.Count;
			List<ScrollViewCell> removedCells = _activeCells.FindAll(cell => cell != null && cell.info != null && IsCellInsideViewPort(cell.info) == false);

			foreach (var cell in removedCells)
			{
				DeactiveCell(cell);
			}

			float startViewPosition = _contentOffsetMax.y;
			for (int i = firstCellIndex - 1; i >= 0; i--)
			{
				if (_cellInfos[i].position + _cellInfos[i].size < startViewPosition)
				{
					break;
				}

				if (IsCellInsideViewPort(_cellInfos[i]))
				{
					AddActiveCell(_cellInfos[i], true);
				}
			}
		}

		void UpdateCellVisibility()
		{
			if (!CanAccessView())
				return;

			if (_cellInfos.Count == 0)
				return;

			float startViewPosition = _viewContent.offsetMax.y;
			float endViewPosition = startViewPosition + _viewportHeight;
			int minIndex = 0;
			int maxIndex = _cellInfos.Count - 1;
			int findIndex = 0;

			while (true)
			{
				if (Mathf.Abs(maxIndex - minIndex) <= 1)
					break;

				findIndex = (minIndex + maxIndex) / 2;

				if (_cellInfos[findIndex].position > endViewPosition)
				{
					maxIndex = findIndex;
				}
				else if (_cellInfos[findIndex].position < startViewPosition)
				{
					minIndex = findIndex;
				}
				else
				{
					break;
				}
			}

			for (int i = findIndex; i < _cellInfos.Count; i++)
			{
				if (IsCellInsideViewPort(_cellInfos[i]))
				{
					// Active Cell
					AddActiveCell(_cellInfos[i]);
				}
				else
				{
					break;
				}
			}

			for (int i = findIndex - 1; i >= 0; i--)
			{
				if (IsCellInsideViewPort(_cellInfos[i]))
				{
					// Active Cell
					AddActiveCell(_cellInfos[i], true);
				}
				else
				{
					break;
				}
			}
		}

		protected void NotifyCellSelected(ScrollViewCell cell)
		{
			if (_delegate == null || cell == null || cell.info == null)
				return;

			_delegate.ScrollCellSelected(cell.info.index);
		}

		protected void AddActiveCell(ScrollViewCellInfo cellInfo, bool isFirst = false)
		{
			if (_delegate == null || cellInfo == null || !CanAccessView())
				return;

			ScrollViewCell cell = _delegate.ScrollCellCreated(cellInfo.index);
			if (cell == null)
				return;

			cell.info = cellInfo;

			if (_scrollOrientation == ScrollOrientation.Vertical)
			{
				cell.SetTopPositionAndHeight(cellInfo.position, cellInfo.size);
			}
			else if (_scrollOrientation == ScrollOrientation.Horizontal)
			{

			}

			if (isFirst)
			{
				_activeCells.Insert(0, cell);
			}
			else
			{
				_activeCells.Add(cell);
			}

			_delegate.ScrollCellWillDisplay(cell);
		}

		public ScrollViewCell CreateCell(string identifier)
		{
			if (!CanAccessView())
				return null;

			if (!_cellPools.ContainsKey(identifier))
			{
				throw new Exception("Could not found cell type with identifier: " + identifier);
			}

			List<ScrollViewCell> cellPool = _cellPools[identifier];
			ScrollViewCell cell = null;

			while (cellPool.Count > 0 && cell == null)
			{
				cell = cellPool[0];
				cellPool.Remove(cell);
			}

			if (cell == null)
			{
				cell = CreateNewCell(identifier);
			}

			return cell;
		}


		ScrollViewCell CreateNewCell(string identifier)
		{
			if (!CanAccessView() || _cellTemplates == null)
				return null;

			ScrollViewCell cellTemplate = _cellTemplates.Find(template => template != null && template.identifier == identifier);
			if (cellTemplate == null)
			{
				throw new Exception("Could not found cell type with identifier: " + identifier);
			}

			ScrollViewCell cell = Instantiate(cellTemplate, _viewContent.transform, false);
			cell.gameObject.SetActive(true);
			cell.transform.localScale = Vector2.one;
			cell.OnCellSelected += NotifyCellSelected;

			RectTransform rectTransform = cell.GetComponent<RectTransform>();
			rectTransform.anchorMin = new Vector2(0, 1);
			rectTransform.anchorMax = new Vector2(1, 1);

			return cell;
		}

		void RecreateCellInfo()
		{
			_poolCellInfo.Return(_cellInfos);
			_cellInfos.Clear();
			int cellCount = Mathf.Max(0, _delegate.ScrollCellCount());

			for (int i = 0; i < cellCount; i++)
			{
				ScrollViewCellInfo cellInfo = _poolCellInfo.Get();
				cellInfo.index = i;
				cellInfo.size = _delegate.ScrollCellSize(cellInfo.index);
				_cellInfos.Add(cellInfo);
			}
		}

		void CalculateCellPosition()
		{
			float position = _headerSpace;
			foreach (var cellInfo in _cellInfos)
			{
				if (cellInfo == null)
					continue;

				cellInfo.position = position;
				position += cellInfo.size + _spacing;
			}
		}

		float UpdateViewContent()
		{
			float viewContentSize = 0;
			foreach (var cellInfo in _cellInfos)
			{
				if (cellInfo == null)
					continue;

				viewContentSize += cellInfo.size;
			}

			viewContentSize += Mathf.Max(0, _cellInfos.Count - 1) * _spacing + _headerSpace + _footerSpace;

			if (!CanAccessView())
				return viewContentSize;

			if (_scrollOrientation == ScrollOrientation.Vertical)
			{
				_viewContent.sizeDelta = new Vector2(_viewContent.sizeDelta.x, viewContentSize);
				if (viewContentSize < _viewportHeight)
				{
					MoveViewToTop(false);
				}
				else if (_viewContent.offsetMin.y + _viewportHeight > 1)
				{
					MoveViewToBottom(false);
				}
			}
			else if (_scrollOrientation == ScrollOrientation.Horizontal)
			{
				_viewContent.sizeDelta = new Vector2(viewContentSize, 0);
			}

			return viewContentSize;
		}

		protected void DeactiveAllCell()
		{
			// Move all cell out of view content, this is extremely faster
			// Disable, enable game objects are very slow
			foreach (var cell in _activeCells)
			{
				if (cell == null)
					continue;

				cell.SetTopPositionAndHeight(-99999, 0);
				cell.info = null;
				
				if (!_cellPools.ContainsKey(cell.identifier))
				{
					_cellPools[cell.identifier] = new List<ScrollViewCell>();	
				}
				_cellPools[cell.identifier].Add(cell);
			}

			_activeCells.Clear();
		}

		protected void DeactiveCell(ScrollViewCell cell)
		{
			if (cell == null)
			{
				_activeCells.Remove(cell);
				return;
			}

			cell.SetTopPositionAndHeight(-999999, 0);
			cell.info = null;

			if (!_cellPools.ContainsKey(cell.identifier))
			{
				_cellPools[cell.identifier] = new List<ScrollViewCell>();
			}

			_cellPools[cell.identifier].Add(cell);
			_activeCells.Remove(cell);
		}

		protected bool IsCellInsideViewPort(ScrollViewCellInfo cellInfo)
		{
			if (cellInfo == null)
				return false;

			float topViewContent = 0;
			float bottomViewContent = _viewportHeight;
			float topCell = cellInfo.position - _contentOffsetMax.y;
			float bottomCell = cellInfo.position + cellInfo.size - _contentOffsetMax.y;
			return !(topCell > bottomViewContent || bottomCell < topViewContent);
		}

		public bool IsViewAtBottom()
		{
			if (!CanAccessView())
			{
				return false;
			}

			if (_scrollOrientation == ScrollOrientation.Vertical)
			{
				if (Mathf.Abs(_viewContent.offsetMin.y) - _viewportHeight < 1)
					return true;
			}
			else if (_scrollOrientation == ScrollOrientation.Horizontal)
			{

			}

			return false;
		}

		public void MoveViewToTop(bool forceUpdate = true)
		{
			if (!CanAccessView())
				return;

			if (_scrollOrientation == ScrollOrientation.Vertical)
			{
				_scrollRect.normalizedPosition = new Vector2(0, 1);
			}
			else if (_scrollOrientation == ScrollOrientation.Horizontal)
			{

			}

			if (forceUpdate && gameObject.activeInHierarchy)
				OnScrollRectMoveUp();
		}

		public void MoveViewToBottom(bool forceUpdate = true)
		{
			if (!CanAccessView())
				return;

			if (_scrollOrientation == ScrollOrientation.Vertical)
			{
				_scrollRect.normalizedPosition = new Vector2(0, 0);
			}
			else if (_scrollOrientation == ScrollOrientation.Horizontal)
			{

			}

			if (forceUpdate && gameObject.activeInHierarchy)
			{
				OnScrollRectMoveDown();
			}
		}

	        public void MoveViewTo(int cellIndex)
	        {
	            if (!CanAccessView())
	                return;

	            if (_scrollOrientation == ScrollOrientation.Vertical)
	            {
	                ScrollViewCellInfo cellInfo = _cellInfos.Find(ci => ci.index == cellIndex);
	                if (cellInfo == null)
	                {
	                    Debug.LogWarningFormat("Cell index ({0}) is out of range [0, {1}]", cellIndex, _cellInfos.Count - 1);
	                    return;
	                }

	                float halfViewPortHeight = _viewportHeight / 2;
	                float contentHeight = _viewContent.sizeDelta.y;
	                float movableContentHeight = contentHeight - _viewportHeight;
	                if (movableContentHeight <= 0)
	                {
	                    MoveViewToTop(false);
	                    return;
	                }

	                float cellCenterPosY = cellInfo.position + cellInfo.size / 2;
	                float normalizeHeight = (cellCenterPosY - halfViewPortHeight) / movableContentHeight;
	                normalizeHeight = 1.0f - Mathf.Clamp01(normalizeHeight);

	                _scrollRect.normalizedPosition = new Vector2(0, normalizeHeight);
	            }
	            else if (_scrollOrientation == ScrollOrientation.Horizontal)
	            {

	            }

	            if (gameObject.activeInHierarchy)
	            {
	                OnScrollRectChanged(Vector2.zero);
	            }
	        }

		void LazyGetViewportHeight()
		{
			if (_viewportHeight <= 0 && _viewPort != null)
			{
				_viewportHeight = _viewPort.rect.height;
			}
		}

		bool CanAccessView()
		{
			return this != null && !_isDestroyed && _scrollRect != null && _viewPort != null && _viewContent != null;
		}

		void RemoveMissingActiveCells()
		{
			_activeCells.RemoveAll(cell => cell == null || cell.info == null);
		}
	}
}
