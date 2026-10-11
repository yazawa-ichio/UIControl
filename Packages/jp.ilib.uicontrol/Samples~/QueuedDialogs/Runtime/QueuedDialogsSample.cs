using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UIControl.UVMBinding;
using UIControl.Samples;
using UVMBinding.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace UIControl.Adoption
{
	public sealed class QueuedDialogsSample : MonoBehaviour
	{
		[SerializeField]
		private UIStack m_Navigator;
		[SerializeField]
		private GameObject m_DialogPrefab;
		[SerializeField]
		private FocusScope m_HomeFocus;
		[SerializeField]
		private Text m_Result;
		private RequestQueue m_Queue;
		private DefaultPageLoader m_Loader;
		private InputAction m_Back;
		public bool IsRunning { get; private set; }

		private void Start() { Initialize(); }
		public void Initialize()
		{
			if (m_Queue != null) { return; }
			m_HomeFocus.Activate();
			m_Queue = new RequestQueue(m_Navigator);
			m_Loader = new DefaultPageLoader();
			m_Loader.Register("UI/RewardDialog", m_DialogPrefab);
			m_Back = UnityEngine.EventSystems.EventSystem.current?.GetComponent<InputSystemUIInputModule>()?.cancel?.action;
			if (m_Back != null) { m_Back.performed += OnBack; }
		}
		private void OnBack(InputAction.CallbackContext context)
		{
			InputButton.TryInvoke<BackButton>();
		}
		public async void StartFlow()
		{
			if (IsRunning) { return; }
			try { await RunFlow(); }
			catch (Exception exception) { Debug.LogException(exception, this); }
		}
		public async Task<RequestResult<bool>[]> RunFlow()
		{
			Initialize();
			if (IsRunning) { throw new InvalidOperationException("A flow is already running."); }
			IsRunning = true;
			List<DialogModel> models = new List<DialogModel>();
			try
			{
				Task<RequestResult<bool>>[] pending = new Task<RequestResult<bool>>[3];
				for (int i = 0; i < pending.Length; i++)
				{
					string title = "REWARD DELIVERY  /  " + (i + 1) + " OF 3";
					pending[i] = m_Queue.Enqueue<bool>(responder =>
					{
						DialogModel model = new DialogModel(title, () => responder.TryAccept(true), () => responder.TryDismiss());
						models.Add(model);
						return ((IViewModel)model).CreateUIParameter("UI/RewardDialog", m_Loader);
					});
				}
				RequestResult<bool>[] results = await Task.WhenAll(pending);
				if (m_Result != null) { m_Result.text = results[0].Status + "  /  " + results[1].Status + "  /  " + results[2].Status; }
				return results;
			}
			finally
			{
				foreach (DialogModel model in models) { model.Dispose(); }
				IsRunning = false;
			}
		}
		private void OnDisable()
		{
			if (m_Back != null) { m_Back.performed -= OnBack; }
			m_Back = null;
			m_Queue?.Dispose();
			m_Queue = null;
		}
	}
}
