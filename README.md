Google Play game services - Xamarin Android Sample
===========================================
I have taken the playgameservices/android-basic-samples Button Clicker sample app and converted the Native Android Java code into a C# Xamarin sample app. 

# Project
This project demonstrates how to use the Real Time Multiplayer functionality provided by the Google Play Games Services. I have taken the Button Click app from the Google Play Services Examples repository from here:

I have converted the Java Classes and Methods into their C# counterparts.

Disclaimer: I have by no means created an app that adheres to Xamarin Recommended app and programming architecture, conventions and practices. I have simply made the code compliant with C# to show how you can use the Google Play Games Services within a Xamarin app. This project is for illustrative purposes only and to use this project to form a foundation for your own Xamarin project would be an unwise thing to do.

<h2>Contents</h2>

These are the Xamarin samples available demonstrating Google Play game services.

**BaseGameUtils**. Utilities used on all samples, which you can use in your projects too. This is not a stand-alone sample, it's a library project. Note: I have pulled this class into the MainActivity.cs file for simplicty, see Disclaimer above before complaining

**ButtonClicker2000**. Represents the new generation in modern button-clicking excitement. A simple multiplayer game sample that shows how to set up the Google Play real-time multiplayer API, invite friends, automatch, accept invitations, use the waiting room UI, send and receive messages and other multiplayer topics.

<h3>Building</h3>
Step 1: Firstly, set up a your Google Play Services and App to test the app as specified here:
https://developers.google.com/games/services/console/enabling

Note: Use "ButtonClicker" as your app and package name when setting up the project. If you use a different name for your app and package name be sure to replace the package name in AndroidMainfest.xml

Step 2: Make sure to restore all NuGet packages in Xamarin Studio or Visual Studio before trying to Build the project.

Step 3: Make sure to replace the App Id and App Name in the ids.xml with the one you chose when you set up the game in the Google Developer Console in Step 1

Step 4: Build and Deploy !

<h2>Support</h2>

Feel free to raise an issue on this project if something is not working and I'll do my best to help


