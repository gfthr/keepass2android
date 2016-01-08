using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Hardware.Fingerprints;
using Android.OS;
using Android.Preferences;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Java.Lang;
using KeePassLib.Keys;
using KeePassLib.Utility;
using Enum = System.Enum;
using Exception = System.Exception;

namespace keepass2android
{
	[Activity(Label = "@string/app_name",
		ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.KeyboardHidden,
		Theme = "@style/MyTheme_ActionBar", MainLauncher = false)]
	[IntentFilter(new[] { "kp2a.action.FingerprintSetupActivity" }, Categories = new[] { Intent.CategoryDefault })]
	public class FingerprintSetupActivity : LockCloseActivity
	{
		private readonly ActivityDesign _activityDesign;

		public FingerprintSetupActivity(IntPtr javaReference, JniHandleOwnership transfer)
			: base(javaReference, transfer)
	    {
		    _activityDesign = new ActivityDesign(this);
	    }
		public FingerprintSetupActivity()
		{
			_activityDesign = new ActivityDesign(this);
		}

		

		private FingerprintUnlockMode _unlockMode = FingerprintUnlockMode.Disabled;
		private FingerprintUnlockMode _desiredUnlockMode;
		private FingerprintEncryption _enc;
		private RadioButton[] _radioButtons;
		public override bool OnOptionsItemSelected(IMenuItem item)
		{
			switch (item.ItemId)
			{

				case Android.Resource.Id.Home:
					Finish();
					return true;
			}

			return base.OnOptionsItemSelected(item);
		}

		protected override void OnCreate(Bundle savedInstanceState)
		{
			_activityDesign.ApplyTheme();
			base.OnCreate(savedInstanceState);
			SetContentView(Resource.Layout.fingerprint_setup);

			Enum.TryParse(
				PreferenceManager.GetDefaultSharedPreferences(this).GetString(App.Kp2a.GetDb().CurrentFingerprintModePrefKey, ""),
				out _unlockMode);

			_fpIcon = FindViewById<ImageView>(Resource.Id.fingerprint_icon);
			_fpTextView = FindViewById<TextView>(Resource.Id.fingerprint_status);

			SupportActionBar.SetDisplayHomeAsUpEnabled(true);
			SupportActionBar.SetHomeButtonEnabled(true);

			int[] radioButtonIds =
			{
				Resource.Id.radio_fingerprint_quickunlock, Resource.Id.radio_fingerprint_unlock,
				Resource.Id.radio_fingerprint_disabled
			};
			_radioButtons = radioButtonIds.Select(FindViewById<RadioButton>).ToArray();
			_radioButtons[0].Tag = FingerprintUnlockMode.QuickUnlock.ToString();
			_radioButtons[1].Tag = FingerprintUnlockMode.FullUnlock.ToString();
			_radioButtons[2].Tag = FingerprintUnlockMode.Disabled.ToString();
			foreach (RadioButton r in _radioButtons)
			{
				r.CheckedChange += (sender, args) =>
				{
					var rbSender = ((RadioButton) sender);
					if (!rbSender.Checked) return;
					foreach (RadioButton rOther in _radioButtons)
					{
						if (rOther == sender) continue;
						rOther.Checked = false;
					}
					FingerprintUnlockMode newMode;
					Enum.TryParse(rbSender.Tag.ToString(), out newMode);
					ChangeUnlockMode(_unlockMode, newMode);

				};
			}

			CheckCurrentRadioButton();

			int errorId = Resource.String.fingerprint_os_error;
			SetError(errorId);

			FindViewById(Resource.Id.cancel_button).Click += (sender, args) =>
			{
				_enc.StopListening();
				_unlockMode = FingerprintUnlockMode.Disabled; //cancelling a FingerprintEncryption means a new key has been created but not been authenticated to encrypt something. We can't keep the previous state.
				StoreUnlockMode();
				FindViewById(Resource.Id.radio_buttons).Visibility = ViewStates.Visible;
				FindViewById(Resource.Id.fingerprint_auth_container).Visibility = ViewStates.Gone;
				_enc = null;
				CheckCurrentRadioButton();
			};

			FindViewById(Resource.Id.radio_buttons).Visibility = ViewStates.Gone;
			FindViewById(Resource.Id.fingerprint_auth_container).Visibility = ViewStates.Gone;

			if ((int)Build.VERSION.SdkInt >= 23)
				RequestPermissions(new[] { Manifest.Permission.UseFingerprint }, FingerprintPermissionRequestCode);
		}

		string CurrentPreferenceKey
		{
			get { return App.Kp2a.GetDb().CurrentFingerprintPrefKey; }
		}

		private void StoreUnlockMode()
		{
			ISharedPreferencesEditor edit = PreferenceManager.GetDefaultSharedPreferences(this).Edit();
			if (_unlockMode == FingerprintUnlockMode.Disabled)
			{
				edit.PutString(CurrentPreferenceKey, "");
			}
			else
			{
				if (_unlockMode == FingerprintUnlockMode.FullUnlock)
					_enc.StoreEncrypted(App.Kp2a.GetDb().KpDatabase.MasterKey.GetUserKey<KcpPassword>().Password.ReadString(), CurrentPreferenceKey, edit);
				else
					_enc.StoreEncrypted("QuickUnlock" /*some dummy data*/, CurrentPreferenceKey, edit);
			}
			edit.PutString(App.Kp2a.GetDb().CurrentFingerprintModePrefKey, _unlockMode.ToString());
			edit.Commit();
		}

		private void CheckCurrentRadioButton()
		{
			
			foreach (RadioButton r in _radioButtons)
			{
				FingerprintUnlockMode um;
				Enum.TryParse(r.Tag.ToString(), out um);
				if (um == _unlockMode)
					r.Checked = true;
			}
		}

		private void SetError(int errorId)
		{
			var tv = FindViewById<TextView>(Resource.Id.tvFatalError);
			tv.Text = GetString(Resource.String.fingerprint_fatal) + " " + GetString(errorId);
			tv.Visibility = ViewStates.Visible;
		}

		const int FingerprintPermissionRequestCode = 0;
		public override void OnRequestPermissionsResult (int requestCode, string[] permissions, Permission[] grantResults)
		{
			if (requestCode == FingerprintPermissionRequestCode && grantResults[0] == Permission.Granted) 
			{
				FingerprintModule fpModule = new FingerprintModule(this);
				if (!fpModule.FingerprintManager.IsHardwareDetected)
				{
					SetError(Resource.String.fingerprint_hardware_error);
					return;
				}
				if (!fpModule.FingerprintManager.HasEnrolledFingerprints)
				{
					SetError(Resource.String.fingerprint_no_enrolled);
					return;
				}
				FindViewById<TextView>(Resource.Id.tvFatalError).Visibility = ViewStates.Gone;
				FindViewById(Resource.Id.radio_buttons).Visibility = ViewStates.Visible;
				FindViewById(Resource.Id.fingerprint_auth_container).Visibility = ViewStates.Gone;
			}
		}
			

		private void ChangeUnlockMode(FingerprintUnlockMode oldMode, FingerprintUnlockMode newMode)
		{
			if (oldMode == newMode)
				return;

			if (newMode == FingerprintUnlockMode.Disabled)
			{
				_unlockMode = newMode;
				StoreUnlockMode();
				return;
			}

			_desiredUnlockMode = newMode;
			FindViewById(Resource.Id.radio_buttons).Visibility = ViewStates.Gone;
			FindViewById(Resource.Id.fingerprint_auth_container).Visibility = ViewStates.Visible;

			_enc = new FingerprintEncryption(new FingerprintModule(this), CurrentPreferenceKey);
			try
			{
				if (!_enc.InitCipher())
					throw new Exception("Failed to initialize cipher");
				ResetErrorTextRunnable();
				_enc.StartListening(new SetupCallback(this));
			}
			catch (Exception e)
			{
				CheckCurrentRadioButton();
				Toast.MakeText(this, e.ToString(), ToastLength.Long).Show();
				FindViewById(Resource.Id.radio_buttons).Visibility = ViewStates.Visible;
				FindViewById(Resource.Id.fingerprint_auth_container).Visibility = ViewStates.Gone;
			}
			

		}

		static readonly long ERROR_TIMEOUT_MILLIS = 1600;
		static readonly long SUCCESS_DELAY_MILLIS = 1300;
		private ImageView _fpIcon;
		private TextView _fpTextView;
		public void OnAuthSucceeded()
		{
			_unlockMode = _desiredUnlockMode;

			_fpTextView.RemoveCallbacks(ResetErrorTextRunnable);
			_fpIcon.SetImageResource(Resource.Drawable.ic_fingerprint_success);
			_fpTextView.SetTextColor(_fpTextView.Resources.GetColor(Resource.Color.success_color, null));
			_fpTextView.Text = _fpTextView.Resources.GetString(Resource.String.fingerprint_success);
			_fpIcon.PostDelayed(() =>
			{
				FindViewById(Resource.Id.radio_buttons).Visibility = ViewStates.Visible;
				FindViewById(Resource.Id.fingerprint_auth_container).Visibility = ViewStates.Gone;

				StoreUnlockMode();

			}, SUCCESS_DELAY_MILLIS);

			
		}


		public void OnFingerprintError(string error)
		{
			_fpIcon.SetImageResource(Resource.Drawable.ic_fingerprint_error);
			_fpTextView.Text = error;
			_fpTextView.SetTextColor(
				_fpTextView.Resources.GetColor(Resource.Color.warning_color, null));
			_fpTextView.RemoveCallbacks(ResetErrorTextRunnable);
			_fpTextView.PostDelayed(ResetErrorTextRunnable, ERROR_TIMEOUT_MILLIS);
		}

		void ResetErrorTextRunnable()
		{
			_fpTextView.SetTextColor(
				_fpTextView.Resources.GetColor(Resource.Color.hint_color, null));
			_fpTextView.Text = _fpTextView.Resources.GetString(Resource.String.fingerprint_hint);
			_fpIcon.SetImageResource(Resource.Drawable.ic_fp_40px);
		}

		protected override void OnResume()
		{
			base.OnResume();
			if (_enc != null)
				_enc.StartListening(new SetupCallback(this));
		}

		protected override void OnPause()
		{
			base.OnPause();
			if (_enc != null)
				_enc.StopListening();
		}
	}

	internal class SetupCallback : FingerprintManager.AuthenticationCallback
	{
		private readonly FingerprintSetupActivity _fingerprintSetupActivity;

		public SetupCallback(FingerprintSetupActivity fingerprintSetupActivity)
		{
			_fingerprintSetupActivity = fingerprintSetupActivity;
		}

		public override void OnAuthenticationSucceeded(FingerprintManager.AuthenticationResult result)
		{
			_fingerprintSetupActivity.OnAuthSucceeded();
		}

		public override void OnAuthenticationError(FingerprintState errorCode, ICharSequence errString)
		{
			_fingerprintSetupActivity.OnFingerprintError(errString.ToString());
		}

		public override void OnAuthenticationHelp(FingerprintState helpCode, ICharSequence helpString)
		{
			_fingerprintSetupActivity.OnFingerprintError(helpString.ToString());
		}

		public override void OnAuthenticationFailed()
		{
			_fingerprintSetupActivity.OnFingerprintError(_fingerprintSetupActivity.Resources.GetString(Resource.String.fingerprint_not_recognized));
		}
	}
}