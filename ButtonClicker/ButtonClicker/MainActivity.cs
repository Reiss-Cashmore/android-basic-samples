using Android.App;
using Android.Widget;
using Android.OS;
using Android.Gms.Games.MultiPlayer.RealTime;

using Android.Gms.Common.Apis;
using System.Collections.Generic;
using Android.Gms.Games.MultiPlayer;
using Android.Views;
using Android.Content;
using Android.Util;
using Android.Gms.Games;
using Android.Gms.Common.Util;
using Android.Gms.Common;
using System.Text;
using System.Timers;
using System.Reflection;
using System;

namespace ButtonClicker
{
	[Activity(Label = "ButtonClicker", MainLauncher = true, Icon = "@mipmap/icon")]
	public class MainActivity : Activity, GoogleApiClient.IConnectionCallbacks, GoogleApiClient.IOnConnectionFailedListener,
		View.IOnClickListener, IRealTimeMessageReceivedListener,
		IRoomStatusUpdateListener, IRoomUpdateListener, IOnInvitationReceivedListener
	{
		/*
		 * API INTEGRATION SECTION. This section contains the code that integrates
		 * the game with the Google Play game services API.
		 */

		const string TAG = "ButtonClicker2000";

		// Request codes for the UIs that we show with startActivityForResult:
		const int RC_SELECT_PLAYERS = 10000;
		const int RC_INVITATION_INBOX = 10001;
		const int RC_WAITING_ROOM = 10002;

		// Request code used to invoke sign in user interactions.
		private
		const int RC_SIGN_IN = 9001;

		// Client used to interact with Google APIs.
		private GoogleApiClient mGoogleApiClient;

		// Are we currently resolving a connection failure?
		private bool mResolvingConnectionFailure = false;

		// Has the user clicked the sign-in button?
		private bool mSignInClicked = false;

		// Set to true to automatically start the sign in flow when the Activity starts.
		// Set to false to require the user to click the button in order to sign in.
		private bool mAutoStartSignInFlow = true;

		// Room ID where the currently active game is taking place; null if we're
		// not playing.
		string mRoomId = null;

		// Are we playing in multiplayer mode?
		bool mMultiplayer = false;

		// The participants in the currently active game
		IList<IParticipant> mParticipants = null;

		// My participant ID in the currently active game
		string mMyId = null;

		// If non-null, this is the id of the invitation we received via the
		// invitation listener
		string mIncomingInvitationId = null;

		// Message buffer for sending messages
		byte[] mMsgBuf = new byte[2];

		// event handlers.
		static int[] CLICKABLES = {
				Resource.Id.button_accept_popup_invitation,
				Resource.Id.button_invite_players,
				Resource.Id.button_quick_game,
				Resource.Id.button_see_invitations,
				Resource.Id.button_sign_in,
				Resource.Id.button_sign_out,
				Resource.Id.button_click_me,
				Resource.Id.button_single_player,
				Resource.Id.button_single_player_2
			};



		int count = 1;

		protected override void OnCreate(Bundle savedInstanceState)
		{

			base.OnCreate(savedInstanceState);

			// Set our view from the "main" layout resource
			SetContentView(Resource.Layout.Main);
			mGoogleApiClient = new GoogleApiClient.Builder(this).AddConnectionCallbacks(this).AddOnConnectionFailedListener(this).AddApi(GamesClass.API).AddScope(GamesClass.ScopeGames).Build();
			foreach (int id in CLICKABLES)
			{
				FindViewById(id).SetOnClickListener(this);
			}

		}

		void StartQuickGame()
		{
			// quick-start a game with 1 randomly selected opponent
			int MIN_OPPONENTS = 1, MAX_OPPONENTS = 1;
			Bundle autoMatchCriteria = RoomConfig.CreateAutoMatchCriteria(MIN_OPPONENTS,
				MAX_OPPONENTS, 0);
			RoomConfig.Builder rtmConfigBuilder = RoomConfig.InvokeBuilder(this);
			rtmConfigBuilder.SetMessageReceivedListener(this);
			rtmConfigBuilder.SetRoomStatusUpdateListener(this);
			rtmConfigBuilder.SetAutoMatchCriteria(autoMatchCriteria);
			SwitchToScreen(Resource.Id.screen_wait);
			KeepScreenOn();
			ResetGameVars();
			GamesClass.RealTimeMultiplayer.Create(mGoogleApiClient, rtmConfigBuilder.Build());
		}

		public void OnClick(View v)
		{
			Intent intent;

			switch (v.Id)
			{
				case Resource.Id.button_single_player:
				case Resource.Id.button_single_player_2:
					// play a single-player game
					ResetGameVars();
					StartGame(false);
					break;
				case Resource.Id.button_sign_in:
					// start the sign-in flow
					Log.Debug(TAG, "Sign-in button clicked");
					mSignInClicked = true;
					mGoogleApiClient.Connect();
					break;
				case Resource.Id.button_sign_out:
					// user wants to sign out
					// sign out.
					Log.Debug(TAG, "Sign-out button clicked");
					mSignInClicked = false;

					GamesClass.SignOut(mGoogleApiClient);
					mGoogleApiClient.Disconnect();
					SwitchToScreen(Resource.Id.screen_sign_in);
					break;
				case Resource.Id.button_invite_players:
					// show list of invitable players
					intent = GamesClass.RealTimeMultiplayer.GetSelectOpponentsIntent(mGoogleApiClient, 1, 3);
					SwitchToScreen(Resource.Id.screen_wait);
					StartActivityForResult(intent, RC_SELECT_PLAYERS);
					break;
				case Resource.Id.button_see_invitations:
					// show list of pending invitations
					intent = GamesClass.Invitations.GetInvitationInboxIntent(mGoogleApiClient);
					SwitchToScreen(Resource.Id.screen_wait);
					StartActivityForResult(intent, RC_INVITATION_INBOX);
					break;
				case Resource.Id.button_accept_popup_invitation:
					// user wants to accept the invitation shown on the invitation popup
					// (the one we got through the OnInvitationReceivedListener).
					AcceptInviteToRoom(mIncomingInvitationId);
					mIncomingInvitationId = null;
					break;
				case Resource.Id.button_quick_game:
					// user wants to play against a random opponent right now
					StartQuickGame();
					break;
				case Resource.Id.button_click_me:

					ScoreOnePoint();
					break;
			}
		}

		protected override void OnActivityResult(int requestCode, Result resultCode,
			Intent data)
		{
			base.OnActivityResult(requestCode, resultCode, data);

			switch (requestCode)
			{
				case RC_SELECT_PLAYERS:
					// we got the result from the "select players" UI -- ready to create the room
					HandleSelectPlayersResult(resultCode, data);
					break;
				case RC_INVITATION_INBOX:
					// we got the result from the "select invitation" UI (invitation inbox). We're
					// ready to accept the selected invitation:
					HandleInvitationInboxResult(resultCode, data);
					break;
				case RC_WAITING_ROOM:
					// we got the result from the "waiting room" UI.
					if (resultCode == Result.Ok)
					{
						// ready to start playing
						Log.Debug(TAG, "Starting game (waiting room returned OK).");
						StartGame(true);
					}
					else if ((int)resultCode == GamesActivityResultCodes.ResultLeftRoom)
					{
						// player indicated that they want to leave the room
						LeaveRoom();
					}
					else if (resultCode == Result.Canceled)
					{
						// Dialog was cancelled (user pressed back key, for instance). In our game,
						// this means leaving the room too. In more elaborate games, this could mean
						// something else (like minimizing the waiting room UI).
						LeaveRoom();
					}
					break;
				case RC_SIGN_IN:
					Log.Debug(TAG, "onActivityResult with requestCode == RC_SIGN_IN, responseCode=" +
						resultCode + ", intent=" + data);
					mSignInClicked = false;
					mResolvingConnectionFailure = false;
					if (resultCode == Result.Ok)
					{
						mGoogleApiClient.Connect();
					}
					else {
						BaseGameUtils.ShowActivityResultError(this, requestCode, (int)resultCode, Resource.String.signin_other_error);
					}
					break;
			}
			base.OnActivityResult(requestCode, resultCode, data);
		}

		// Handle the result of the "Select players UI" we launched when the user clicked the
		// "Invite friends" button. We react by creating a room with those players.
		private void HandleSelectPlayersResult(Result response, Intent data)
		{
			if (response != Result.Ok)
			{
				Log.Warn(TAG, "*** select players UI cancelled, " + response);
				SwitchToMainScreen();
				return;
			}

			Log.Debug(TAG, "Select players UI succeeded.");

			// get the invitee list
			var invitees = data.GetStringArrayListExtra(GamesClass.ExtraPlayerIds);
			Log.Debug(TAG, "Invitee count: " + invitees.Count);

			// get the automatch criteria
			Bundle autoMatchCriteria = null;
			int minAutoMatchPlayers = data.GetIntExtra(Multiplayer.ExtraMinAutomatchPlayers, 0);
			int maxAutoMatchPlayers = data.GetIntExtra(Multiplayer.ExtraMaxAutomatchPlayers, 0);
			if (minAutoMatchPlayers > 0 || maxAutoMatchPlayers > 0)
			{
				autoMatchCriteria = RoomConfig.CreateAutoMatchCriteria(
					minAutoMatchPlayers, maxAutoMatchPlayers, 0);
				Log.Debug(TAG, "Automatch criteria: " + autoMatchCriteria);
			}

			// create the room
			Log.Debug(TAG, "Creating room...");
			RoomConfig.Builder rtmConfigBuilder = RoomConfig.InvokeBuilder(this);
			rtmConfigBuilder.AddPlayersToInvite(invitees);
			rtmConfigBuilder.SetMessageReceivedListener(this);
			rtmConfigBuilder.SetRoomStatusUpdateListener(this);
			if (autoMatchCriteria != null)
			{
				rtmConfigBuilder.SetAutoMatchCriteria(autoMatchCriteria);
			}
			SwitchToScreen(Resource.Id.screen_wait);
			KeepScreenOn();
			ResetGameVars();
			GamesClass.RealTimeMultiplayer.Create(mGoogleApiClient, rtmConfigBuilder.Build());
			Log.Debug(TAG, "Room created, waiting for it to be ready...");
		}

		// Handle the result of the invitation inbox UI, where the player can pick an invitation
		// to accept. We react by accepting the selected invitation, if any.
		private void HandleInvitationInboxResult(Result response, Intent data)
		{
			if (response != Result.Ok)
			{
				Log.Warn(TAG, "*** invitation inbox UI cancelled, " + response);
				SwitchToMainScreen();
				return;
			}

			Log.Debug(TAG, "Invitation inbox UI succeeded.");
			IInvitation inv = (IInvitation)data.Extras.GetParcelable(Multiplayer.ExtraInvitation);

			// accept invitation
			AcceptInviteToRoom(inv.InvitationId);
		}

		// Accept the given invitation.
		void AcceptInviteToRoom(string invId)
		{
			// accept the invitation
			Log.Debug(TAG, "Accepting invitation: " + invId);
			RoomConfig.Builder roomConfigBuilder = RoomConfig.InvokeBuilder(this);
			roomConfigBuilder.SetInvitationIdToAccept(invId)
				.SetMessageReceivedListener(this)
				.SetRoomStatusUpdateListener(this);
			SwitchToScreen(Resource.Id.screen_wait);
			KeepScreenOn();
			ResetGameVars();
			GamesClass.RealTimeMultiplayer.Join(mGoogleApiClient, roomConfigBuilder.Build());
		}


		protected override void OnStop()
		{
			Log.Debug(TAG, "**** got onStop");

			// if we're in a room, leave it.
			LeaveRoom();

			// stop trying to keep the screen on
			StopKeepingScreenOn();

			if (mGoogleApiClient != null && mGoogleApiClient.IsConnected)
			{
				SwitchToMainScreen();
			}
			else {
				SwitchToScreen(Resource.Id.screen_sign_in);
			}
			base.OnStop();
		}


		protected override void OnStart()
		{
			if (mGoogleApiClient == null)
			{
				SwitchToScreen(Resource.Id.screen_sign_in);
			}
			else if (!mGoogleApiClient.IsConnected)
			{
				Log.Debug(TAG, "Connecting client.");
				SwitchToScreen(Resource.Id.screen_wait);
				mGoogleApiClient.Connect();
			}
			else {
				Log.Warn(TAG,
					"GameHelper: client was already connected on onStart()");
			}
			base.OnStart();
		}
		public override bool OnKeyDown(Keycode keyCode, KeyEvent e)
		{
			if (keyCode == KeyEvent.KeyCodeFromString("KEYCODE_BACK") && mCurScreen == Resource.Id.screen_game)
			{
				LeaveRoom();
				return true;
			}
			return base.OnKeyDown(keyCode, e);
		}

		// Leave the room.
		void LeaveRoom()
		{
			Log.Debug(TAG, "Leaving room.");
			mSecondsLeft = 0;
			StopKeepingScreenOn();
			if (mRoomId != null)
			{
				GamesClass.RealTimeMultiplayer.Leave(mGoogleApiClient, this, mRoomId);
				mRoomId = null;
				SwitchToScreen(Resource.Id.screen_wait);
			}
			else {
				SwitchToMainScreen();
			}
		}


		// Show the waiting room UI to track the progress of other players as they enter the
		// room and get connected.
		void ShowWaitingRoom(IRoom room)
		{
			// minimum number of players required for our game
			// For simplicity, we require everyone to join the game before we start it
			// (this is signaled by Integer.MAX_VALUE).
			int MIN_PLAYERS = int.MaxValue;
			Intent i = GamesClass.RealTimeMultiplayer.GetWaitingRoomIntent(mGoogleApiClient, room, MIN_PLAYERS);

			// show waiting room UI
			StartActivityForResult(i, RC_WAITING_ROOM);
		}

		// Called when we get an invitation to play a game. We react by showing that to the user.

		public void OnInvitationReceived(IInvitation invitation)
		{
			// We got an invitation to play a game! So, store it in
			// mIncomingInvitationId
			// and show the popup on the screen.
			mIncomingInvitationId = invitation.InvitationId;
			var textView = (TextView)FindViewById(Resource.Id.incoming_invitation_text);
			textView.SetText(invitation.Inviter.DisplayName + " " + GetString(Resource.String.is_inviting_you), TextView.BufferType.Normal);
			SwitchToScreen(mCurScreen); // This will show the invitation popup
		}


		public void OnInvitationRemoved(string invitationId)
		{

			if (mIncomingInvitationId.Equals(invitationId) && mIncomingInvitationId != null)
			{
				mIncomingInvitationId = null;
				SwitchToScreen(mCurScreen); // This will hide the invitation popup
			}

		}

		/*
		 * CALLBACKS SECTION. This section shows how we implement the several games
		 * API callbacks.
		 */


		public void OnConnected(Bundle connectionHint)
		{
			Log.Debug(TAG, "onConnected() called. Sign in successful!");

			Log.Debug(TAG, "Sign-in succeeded.");

			// register listener so we are notified if we receive an invitation to play
			// while we are in the game
			GamesClass.Invitations.RegisterInvitationListener(mGoogleApiClient, this);

			if (connectionHint != null)
			{
				Log.Debug(TAG, "onConnected: connection hint ed. Checking for e.");
				IInvitation inv = (IInvitation)connectionHint
					.GetParcelable(Multiplayer.ExtraInvitation);
				if (inv != null && inv.InvitationId != null)
				{
					// retrieve and cache the invitation ID
					Log.Debug(TAG, "onConnected: connection hint has a room invite!");
					AcceptInviteToRoom(inv.InvitationId);
					return;
				}
			}
			SwitchToMainScreen();

		}




		public void OnConnectionSuspended(int i)
		{
			Log.Debug(TAG, "onConnectionSuspended() called. Trying to reconnect.");
			mGoogleApiClient.Connect();
		}


		public void OnConnectionFailed(ConnectionResult connectionResult)
		{
			Log.Debug(TAG, "onConnectionFailed() called, result: " + connectionResult);

			if (mResolvingConnectionFailure)
			{
				Log.Debug(TAG, "onConnectionFailed() ignoring connection failure; already resolving.");
				return;
			}

			if (mSignInClicked || mAutoStartSignInFlow)
			{
				mAutoStartSignInFlow = false;
				mSignInClicked = false;
				mResolvingConnectionFailure = BaseGameUtils.ResolveConnectionFailure(this, mGoogleApiClient,
					connectionResult, RC_SIGN_IN, GetString(Resource.String.signin_other_error));
			}

			SwitchToScreen(Resource.Id.screen_sign_in);
		}

		// Called when we are connected to the room. We're not ready to play yet! (maybe not everybody
		// is connected yet).

		public void OnConnectedToRoom(IRoom room)
		{
			Log.Debug(TAG, "onConnectedToRoom.");

			//get participants and my ID:
			mParticipants = room.Participants;
			mMyId = room.GetParticipantId(GamesClass.Players.GetCurrentPlayerId(mGoogleApiClient));

			// save room ID if its not initialized in onRoomCreated() so we can leave cleanly before the game starts.
			if (mRoomId == null)
				mRoomId = room.RoomId;

			// print out the list of participants (for debug purposes)
			Log.Debug(TAG, "Room ID: " + mRoomId);
			Log.Debug(TAG, "My ID " + mMyId);
			Log.Debug(TAG, "<< CONNECTED TO ROOM>>");
		}

		// Called when we've successfully left the room (this happens a result of voluntarily leaving
		// via a call to LeaveRoom(). If we get disconnected, we get onDisconnectedFromRoom()).

		public void OnLeftRoom(int statusCode, string roomId)
		{
			// we have left the room; return to main screen.
			Log.Debug(TAG, "onLeftRoom, code " + statusCode);
			SwitchToMainScreen();
		}

		// Called when we get disconnected from the room. We return to the main screen.

		public void OnDisconnectedFromRoom(IRoom room)
		{
			mRoomId = null;
			ShowGameError();
		}

		// Show error message about game being cancelled and return to main screen.
		void ShowGameError()
		{
			BaseGameUtils.MakeSimpleDialog(this, GetString(Resource.String.game_problem));
			SwitchToMainScreen();
		}

		// Called when room has been created

		public void OnRoomCreated(int statusCode, IRoom room)
		{
			Log.Debug(TAG, "onRoomCreated(" + statusCode + ", " + room + ")");
			if (statusCode != GamesStatusCodes.StatusOk)
			{
				Log.Error(TAG, "*** Error: onRoomCreated, status " + statusCode);
				ShowGameError();
				return;
			}

			// save room ID so we can leave cleanly before the game starts.
			mRoomId = room.RoomId;

			// show the waiting room UI
			ShowWaitingRoom(room);
		}

		// Called when room is fully connected.

		public void OnRoomConnected(int statusCode, IRoom room)
		{
			Log.Debug(TAG, "onRoomConnected(" + statusCode + ", " + room + ")");
			if (statusCode != GamesStatusCodes.StatusOk)
			{
				Log.Error(TAG, "*** Error: onRoomConnected, status " + statusCode);
				ShowGameError();
				return;
			}
			UpdateRoom(room);
		}


		public void OnJoinedRoom(int statusCode, IRoom room)
		{
			Log.Debug(TAG, "onJoinedRoom(" + statusCode + ", " + room + ")");
			if (statusCode != GamesStatusCodes.StatusOk)
			{
				Log.Error(TAG, "*** Error: onRoomConnected, status " + statusCode);
				ShowGameError();
				return;
			}

			// show the waiting room UI
			ShowWaitingRoom(room);
		}


		// We treat most of the room update callbacks in the same way: we update our list of
		// participants and update the display. In a real game we would also have to check if that
		// change requires some action like removing the corresponding player avatar from the screen,
		// etc.

		public void OnPeerDeclined(IRoom room, IList<string> arg1)
		{
			UpdateRoom(room);
		}


		public void OnPeerInvitedToRoom(IRoom room, IList<string> arg1)
		{
			UpdateRoom(room);
		}


		public void OnP2PDisconnected(string participant)
		{
		}


		public void OnP2PConnected(string participant)
		{
		}


		public void OnPeerJoined(IRoom room, IList<string> arg1)
		{
			UpdateRoom(room);
		}


		public void OnPeerLeft(IRoom room, IList<string> peersWhoLeft)
		{
			UpdateRoom(room);
		}


		public void OnRoomAutoMatching(IRoom room)
		{
			UpdateRoom(room);
		}

		public void OnRoomConnecting(IRoom room)
		{
			UpdateRoom(room);
		}

		public void OnPeersConnected(IRoom room, IList<string> peers)
		{
			UpdateRoom(room);
		}


		public void OnPeersDisconnected(IRoom room, IList<string> peers)
		{
			UpdateRoom(room);
		}

		void UpdateRoom(IRoom room)
		{
			if (room != null)
			{
				mParticipants = room.Participants;
			}
			if (mParticipants != null)
			{
				UpdatePeerScoresDisplay();
			}
		}

		/*
		 * GAME LOGIC SECTION. Methods that implement the game's rules.
		 */

		// Current state of the game:
		int mSecondsLeft = -1; // how long until the game ends (seconds)
		int GAME_DURATION = 20; // game duration, seconds.
		int mScore = 0; // user's current score

		// Reset game variables in preparation for a new game.
		void ResetGameVars()
		{
			mSecondsLeft = GAME_DURATION;
			mScore = 0;
			mParticipantScore.Clear();
			mFinishedParticipants.Clear();
		}


		// Start the gameplay phase of the game.
		void StartGame(bool multiplayer)
		{
			mMultiplayer = multiplayer;
			UpdateScoreDisplay();
			BroadcastScore(false);
			SwitchToScreen(Resource.Id.screen_game);

			FindViewById(Resource.Id.button_click_me).Visibility = ViewStates.Visible;

			// run the gameTick() method every second to update the game.


			updateHandler = new Handler();
			updateCallback = GameTimerTick;

			updateHandler.PostDelayed(updateCallback, 1000);
		}
		Handler updateHandler;
		Action updateCallback;
		// Specify what you want to happen when the Elapsed event is raised.
		private void GameTimerTick()
		{
			if (mSecondsLeft <= 0)
				return;
			GameTick();
			updateHandler.PostDelayed(updateCallback, 1000);
		}


		// Game tick -- update countdown, check if game ended.
		void GameTick()
		{
			if (mSecondsLeft > 0)
				--mSecondsLeft;

			// update countdown
			((TextView)FindViewById(Resource.Id.countdown)).SetText("0:" + (mSecondsLeft < 10 ? "0" : "") + mSecondsLeft.ToString(), TextView.BufferType.Normal);

			if (mSecondsLeft <= 0)
			{
				// finish game
				FindViewById(Resource.Id.button_click_me).Visibility = ViewStates.Visible;
				BroadcastScore(true);
			}
		}

		// indicates the player scored one point
		void ScoreOnePoint()
		{
			if (mSecondsLeft <= 0)
				return; // too late!
			++mScore;
			UpdateScoreDisplay();
			UpdatePeerScoresDisplay();

			// broadcast our new score to our peers
			BroadcastScore(false);
		}

		/*
		 * COMMUNICATIONS SECTION. Methods that implement the game's network
		 * protocol.
		 */

		// Score of other participants. We update this as we receive their scores
		// from the network.
		Dictionary<string, int> mParticipantScore = new Dictionary<string, int>();

		// Participants who sent us their final score.
		HashSet<string> mFinishedParticipants = new HashSet<string>();

		// Called when we receive a real-time message from the network.
		// Messages in our game are made up of 2 bytes: the first one is 'F' or 'U'
		// indicating
		// whether it's a final or interim score. The second byte is the score.
		// There is also the
		// 'S' message, which indicates that the game should start.

		public void OnRealTimeMessageReceived(RealTimeMessage rtm)
		{
			byte[] buf = rtm.GetMessageData();
			string sender = rtm.SenderParticipantId;
			Log.Debug(TAG, "Message received: " + (char)buf[0] + "/" + (int)buf[1]);

			if (buf[0] == 'F' || buf[0] == 'U')
			{
				int participantScore = 0;
				mParticipantScore.TryGetValue(sender, out participantScore);
				// score update.
				int existingScore = mParticipantScore.ContainsKey(sender) ?
													 participantScore : 0;
				int thisScore = (int)buf[1];
				if (thisScore > existingScore)
				{
					// this check is necessary because packets may arrive out of
					// order, so we
					// should only ever consider the highest score we received, as
					// we know in our
					// game there is no way to lose points. If there was a way to
					// lose points,
					// we'd have to add a "serial number" to the packet.
					mParticipantScore[sender] = thisScore;
				}

				// update the scores on the screen
				UpdatePeerScoresDisplay();

				// if it's a final score, mark this participant as having finished
				// the game
				if ((char)buf[0] == 'F')
				{
					mFinishedParticipants.Add(rtm.SenderParticipantId);
				}
			}
		}

		// Broadcast my score to everybody else.
		void BroadcastScore(bool finalScore)
		{
			if (!mMultiplayer)
				return; // playing single-player mode

			// First byte in message indicates whether it's a final score or not
			mMsgBuf[0] = (byte)(finalScore ? 'F' : 'U');

			// Second byte is the score.
			mMsgBuf[1] = (byte)mScore;

			// Send to every other participant.
			foreach (IParticipant p in mParticipants)
			{
				if (p.ParticipantId.Equals(mMyId))
					continue;
				if (p.Status != Participant.StatusJoined)
					continue;
				if (finalScore)
				{
					// final score notification must be sent via reliable message
					GamesClass.RealTimeMultiplayer.SendReliableMessage(mGoogleApiClient, null, mMsgBuf,
																	   mRoomId, p.ParticipantId);
				}
				else {
					// it's an interim score notification, so we can use unreliable
					GamesClass.RealTimeMultiplayer.SendUnreliableMessage(mGoogleApiClient, mMsgBuf, mRoomId,
																		 p.ParticipantId);
				}
			}
		}

		/*
		 * UI SECTION. Methods that implement the game's UI.
		 */



		// This array lists all the individual screens our game has.
		int[] SCREENS = {
			Resource.Id.screen_game, Resource.Id.screen_main, Resource.Id.screen_sign_in,
			Resource.Id.screen_wait
	};
		int mCurScreen = -1;

		void SwitchToScreen(int screenId)
		{
			// make the requested screen visible; hide all others.
			foreach (int id in SCREENS)
			{
				if (screenId == id)
				{
					FindViewById(id).Visibility = ViewStates.Visible;
				}
				else {
					FindViewById(id).Visibility = ViewStates.Gone;
				}
			}
			mCurScreen = screenId;

			// should we show the invitation popup?
			bool showInvPopup;
			if (mIncomingInvitationId == null)
			{
				// no invitation, so no popup
				showInvPopup = false;
			}
			else if (mMultiplayer)
			{
				// if in multiplayer, only show invitation on main screen
				showInvPopup = (mCurScreen == Resource.Id.screen_main);
			}
			else {
				// single-player: show on main screen and gameplay screen
				showInvPopup = (mCurScreen == Resource.Id.screen_main || mCurScreen == Resource.Id.screen_game);
			}
			if (showInvPopup)
			{
				FindViewById(Resource.Id.invitation_popup).Visibility = ViewStates.Visible;
			}
			else {
				FindViewById(Resource.Id.invitation_popup).Visibility = ViewStates.Gone;
			}
		}

		void SwitchToMainScreen()
		{
			if (mGoogleApiClient != null && mGoogleApiClient.IsConnected)
			{
				SwitchToScreen(Resource.Id.screen_main);
			}
			else {
				SwitchToScreen(Resource.Id.screen_sign_in);
			}
		}

		// updates the label that shows my score
		void UpdateScoreDisplay()
		{
			((TextView)FindViewById(Resource.Id.my_score)).SetText(FormatScore(mScore), TextView.BufferType.Normal);
		}

		// formats a score as a three-digit number
		string FormatScore(int i)
		{
			if (i < 0)
				i = 0;
			string s = i.ToString();
			return s.Length == 1 ? "00" + s : s.Length == 2 ? "0" + s : s;
		}

		// updates the screen with the scores from our peers
		void UpdatePeerScoresDisplay()
		{
			((TextView)FindViewById(Resource.Id.score0)).SetText(FormatScore(mScore) + " - Me", TextView.BufferType.Normal);
			int[] arr = {
				Resource.Id.score1, Resource.Id.score2, Resource.Id.score3
		};
			int i = 0;

			if (mRoomId != null)
			{
				foreach (IParticipant p in mParticipants)
				{
					string pid = p.ParticipantId;
					if (pid.Equals(mMyId))
						continue;
					if (p.Status != Participant.StatusJoined)
						continue;

					int participantScore = 0;
					mParticipantScore.TryGetValue(pid, out participantScore);
					int score = mParticipantScore.ContainsKey(pid) ? participantScore : 0;
					((TextView)FindViewById(arr[i])).SetText(FormatScore(score) + " - " +
															 p.DisplayName, TextView.BufferType.Normal);
					++i;
				}
			}

			for (; i < arr.Length; ++i)
			{
				((TextView)FindViewById(arr[i])).SetText("", TextView.BufferType.Normal);
			}
		}

		/*
		 * MISC SECTION. Miscellaneous methods.
		 */


		// Sets the flag to keep this screen on. It's recommended to do that during
		// the
		// handshake when setting up a game, because if the screen turns off, the
		// game will be
		// cancelled.
		void KeepScreenOn()
		{
			Window.AddFlags(WindowManagerFlags.KeepScreenOn);
		}

		// Clears the flag that keeps the screen on.
		void StopKeepingScreenOn()
		{
			Window.ClearFlags(WindowManagerFlags.KeepScreenOn);
		}

	}




	public class BaseGameUtils
	{

		/**
		 * Show an {@link android.app.AlertDialog} with an 'OK' button and a message.
		 *
		 * @param activity the Activity in which the Dialog should be displayed.
		 * @param message the message to display in the Dialog.
		 */
		public static void ShowAlert(Activity activity, string message)
		{
			(new AlertDialog.Builder(activity)).SetMessage(message)
											   .SetNeutralButton(Android.Resource.String.Ok, (IDialogInterfaceOnClickListener)null).Create().Show();
		}

		/**
		 * Resolve a connection failure from
		 * {@link com.google.android.gms.common.api.GoogleApiClient.OnConnectionFailedListener#onConnectionFailed(com.google.android.gms.common.ConnectionResult)}
		 *
		 * @param activity the Activity trying to resolve the connection failure.
		 * @param client the GoogleAPIClient instance of the Activity.
		 * @param result the ConnectionResult received by the Activity.
		 * @param requestCode a request code which the calling Activity can use to identify the result
		 *                    of this resolution in onActivityResult.
		 * @param fallbackErrorMessage a generic error message to display if the failure cannot be resolved.
		 * @return true if the connection failure is resolved, false otherwise.
		 */
		public static bool ResolveConnectionFailure(Activity activity,
			GoogleApiClient client, ConnectionResult result, int requestCode,
			string fallbackErrorMessage)
		{

			if (result.HasResolution)
			{
				try
				{
					result.StartResolutionForResult(activity, requestCode);
					return true;
				}
				catch (IntentSender.SendIntentException e)
				{
					// The intent was canceled before it was sent.  Return to the default
					// state and attempt to connect to get an updated ConnectionResult.
					client.Connect();
					return false;
				}
			}
			else {
				// not resolvable... so show an error message
				int errorCode = result.ErrorCode;
				Dialog dialog = GooglePlayServicesUtil.GetErrorDialog(errorCode,
					activity, requestCode);
				if (dialog != null)
				{
					dialog.Show();
				}
				else {
					// no built-in dialog: show the fallback error message
					ShowAlert(activity, fallbackErrorMessage);
				}
				return false;
			}
		}


		/**
		 * Show a {@link android.app.Dialog} with the correct message for a connection error.
		 *  @param activity the Activity in which the Dialog should be displayed.
		 * @param requestCode the request code from onActivityResult.
		 * @param actResp the response code from onActivityResult.
		 * @param errorDescription the resource id of a String for a generic error message.
		 */
		public static void ShowActivityResultError(Activity activity, int requestCode, int actResp, int errorDescription)
		{
			if (activity == null)
			{
				Log.Error("BaseGameUtils", "*** No Activity. Can't show failure dialog!");
				return;
			}
			Dialog errorDialog;

			switch (actResp)
			{
				case GamesActivityResultCodes.ResultAppMisconfigured:
					errorDialog = MakeSimpleDialog(activity,
						activity.GetString(Resource.String.app_misconfigured));
					break;
				case GamesActivityResultCodes.ResultSignInFailed:
					errorDialog = MakeSimpleDialog(activity,
						activity.GetString(Resource.String.sign_in_failed));
					break;
				case GamesActivityResultCodes.ResultLicenseFailed:
					errorDialog = MakeSimpleDialog(activity,
						activity.GetString(Resource.String.license_failed));
					break;
				default:
					// No meaningful Activity response code, so generate default Google
					// Play services dialog
					int errorCode = GooglePlayServicesUtil.IsGooglePlayServicesAvailable(activity);
					errorDialog = GooglePlayServicesUtil.GetErrorDialog(errorCode,
						activity, requestCode, null);
					if (errorDialog == null)
					{
						// get fallback dialog
						Log.Error("BaseGamesUtils",
							"No standard error dialog available. Making fallback dialog.");
						errorDialog = MakeSimpleDialog(activity, activity.GetString(errorDescription));
					}
					break;
			}

			errorDialog.Show();
		}

		/**
		 * Create a simple {@link Dialog} with an 'OK' button and a message.
		 *
		 * @param activity the Activity in which the Dialog should be displayed.
		 * @param text the message to display on the Dialog.
		 * @return an instance of {@link android.app.AlertDialog}
		 */
		public static Dialog MakeSimpleDialog(Activity activity, string text)
		{
			return (new AlertDialog.Builder(activity)).SetMessage(text)
													  .SetNeutralButton(Android.Resource.String.Ok, (IDialogInterfaceOnClickListener)null).Create();
		}

		/**
		 * Create a simple {@link Dialog} with an 'OK' button, a title, and a message.
		 *
		 * @param activity the Activity in which the Dialog should be displayed.
		 * @param title the title to display on the dialog.
		 * @param text the message to display on the Dialog.
		 * @return an instance of {@link android.app.AlertDialog}
		 */
		public static Dialog MakeSimpleDialog(Activity activity, string title, string text)
		{
			return (new AlertDialog.Builder(activity))
				.SetTitle(title)
				.SetMessage(text)
				.SetNeutralButton(Android.Resource.String.Ok, (IDialogInterfaceOnClickListener)null)
				.Create();
		}

	}

}