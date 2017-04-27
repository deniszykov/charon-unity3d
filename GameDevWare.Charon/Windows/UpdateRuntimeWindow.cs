﻿/*
	Copyright (c) 2016 Denis Zykov

	This is part of "Charon: Game Data Editor" Unity Plugin.

	Charon Game Data Editor Unity Plugin is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see http://www.gnu.org/licenses.
*/

using System;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using GameDevWare.Charon.Async;
using GameDevWare.Charon.Utils;
using UnityEditor;
using UnityEngine;
using Coroutine = GameDevWare.Charon.Async.Coroutine;

// ReSharper disable UnusedMember.Local
namespace GameDevWare.Charon.Windows
{
	internal class UpdateRuntimeWindow : EditorWindow
	{
		private static readonly Version MinimalMonoVersion;

		private string monoPath;
		private string runtimeVersion;
		[NonSerialized]
		private Promise checkRuntimeVersionCoroutine;
		[NonSerialized]
		private Promise<RunResult> runMonoTask;
		private bool autoClose;

		private event EventHandler Done;
		private event EventHandler<ErrorEventArgs> Cancel;

		static UpdateRuntimeWindow()
		{
			MinimalMonoVersion = new Version(4, 6, 0);

			
		}
		public UpdateRuntimeWindow()
		{
			this.titleContent = new GUIContent(Resources.UI_UNITYPLUGIN_WINDOWUPDATERUNTIMETITLE);
			this.maxSize = this.minSize = new Vector2(480, 220);
			this.position = new Rect(
				(Screen.width - this.maxSize.x) / 2,
				(Screen.height - this.maxSize.y) / 2,
				this.maxSize.x,
				this.maxSize.y
			);
		}

		// ReSharper disable once InconsistentNaming
		protected void OnGUI()
		{
			EditorGUILayout.HelpBox(Resources.UI_UNITYPLUGIN_WINDOWRUNTIMEREQUIRED + "\r\n\r\n" +
									string.Format(Resources.UI_UNITYPLUGIN_WINDOWFINDMONOMANUALLY) + "\r\n" +
									Resources.UI_UNITYPLUGIN_WINDOWDOWNLOADMONO + "\r\n" +
									(RuntimeInformation.IsWindows ? Resources.UI_UNITYPLUGIN_WINDOWDOWNLOADDOTNET + "\r\n\r\n" : "") +
									Resources.UI_UNITYPLUGIN_WINDOWPRESSHELP, MessageType.Info);

			var checkIsRunning = this.checkRuntimeVersionCoroutine != null && this.checkRuntimeVersionCoroutine.IsCompleted == false;
			GUI.enabled = !checkIsRunning;
			GUILayout.BeginHorizontal();
			EditorGUILayout.Space();

			if (RuntimeInformation.IsWindows && GUILayout.Button(Resources.UI_UNITYPLUGIN_WINDOWDOWNLOADDOTNETBUTTON, GUILayout.Width(140)))
				Application.OpenURL("https://www.microsoft.com/ru-RU/download/details.aspx?id=42643");
			if (GUILayout.Button(Resources.UI_UNITYPLUGIN_WINDOWDOWNLOADMONOBUTTON, GUILayout.Width(140)))
				Application.OpenURL("http://www.mono-project.com/download/#download-mac");
			if (GUILayout.Button(Resources.UI_UNITYPLUGIN_WINDOWHELPBUTTON, GUILayout.Width(40)))
				Application.OpenURL("https://gamedevware.com/docs");
			GUILayout.EndHorizontal();

			GUILayout.Space(18);
			EditorGUILayout.BeginHorizontal();
			{
				this.monoPath = EditorGUILayout.TextField(Resources.UI_UNITYPLUGIN_WINDOWPATHTOMONO, this.monoPath);
				if (GUILayout.Button(Resources.UI_UNITYPLUGIN_WINDOWBROWSEBUTTON, EditorStyles.toolbarButton, GUILayout.Width(70), GUILayout.Height(18)))
				{
					this.monoPath = EditorUtility.OpenFolderPanel(Resources.UI_UNITYPLUGIN_WINDOWPATHTOMONO, "", "");
					GUI.changed = true;
					this.Repaint();
				}
				GUILayout.Space(5);
			}
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.LabelField(Resources.UI_UNITYPLUGIN_WINDOWRUNTIMEVERSION, this.runtimeVersion, new GUIStyle { richText = true });

			GUILayout.Space(18);
			GUILayout.BeginHorizontal();
			EditorGUILayout.Space();
			if (GUILayout.Button(Resources.UI_UNITYPLUGIN_WINDOWRECHECKBUTTON, GUILayout.Width(80)))
				this.RunCheck();

			GUI.enabled = true;
			if (GUILayout.Button(Resources.UI_UNITYPLUGIN_WINDOWCANCELBUTTON, GUILayout.Width(80)))
				this.Close();
			GUILayout.EndHorizontal();

			var canCheckMono = (GUI.changed &&
								string.IsNullOrEmpty(this.monoPath) == false &&
								(File.Exists(Path.Combine(this.monoPath, MonoRuntimeInformation.MonoExecutableName)) ||
									File.Exists(this.monoPath)));

			// ReSharper disable once ConditionIsAlwaysTrueOrFalse
			if (!checkIsRunning && canCheckMono)
				this.RunCheck();
		}

		private void RunCheck()
		{
			this.checkRuntimeVersionCoroutine = new Coroutine(this.CheckRuntimeVersionAsync());
		}
		private IEnumerable CheckRuntimeVersionAsync()
		{

			if (RuntimeInformation.IsWindows)
			{
				var dotNetRuntimeVersion = DotNetRuntimeInformation.GetVersion();
				if (dotNetRuntimeVersion != null)
				{
					this.UpdateRuntimeVersionLabel(dotNetRuntimeVersion, ".NET", true);
					this.RaiseDone(monoRuntimePath: null);
					yield break;
				}
			}

			var monoRuntimePath = File.Exists(this.monoPath) ? this.monoPath : Path.Combine(this.monoPath, MonoRuntimeInformation.MonoExecutableName);
			if (string.IsNullOrEmpty(monoRuntimePath))
				yield break;

			this.runtimeVersion = Resources.UI_UNITYPLUGIN_WINDOWCHECKINGMONO;
			this.runMonoTask = CommandLine.Run(new RunOptions(monoRuntimePath, "--version")
			{
				CaptureStandardOutput = true,
				CaptureStandardError = true,
				ExecutionTimeout = TimeSpan.FromSeconds(5)
			});
			yield return this.runMonoTask.IgnoreFault();

			var output = string.Empty;
			if (this.runMonoTask.HasErrors == false)
				output = this.runMonoTask.GetResult().GetOutputData() ?? "";

			var monoRuntimeVersionMatch = new Regex(@"version (?<v>[0-9]+\.[0-9]+\.[0-9]+)", RegexOptions.Multiline | RegexOptions.IgnoreCase).Match(output);
			if (!monoRuntimeVersionMatch.Success)
			{
				if (Settings.Current.Verbose)
					Debug.LogWarning(output.Length > 0 ? output : string.Format(Resources.UI_UNITYPLUGIN_WINDOWCHECKINGMONOFAILED, this.runMonoTask.GetResult().ExitCode));

				this.UpdateRuntimeVersionLabel(Resources.UI_UNITYPLUGIN_WINDOWRUNTIMEVERSIONUNKNOWN, "Mono", isValid: false);
			}
			else
			{
				try
				{
					var monoRuntimeVersion = new Version(monoRuntimeVersionMatch.Groups["v"].Value);
					this.runtimeVersion = monoRuntimeVersion + " (Mono)";

					if (monoRuntimeVersion >= MinimalMonoVersion)
						this.RaiseDone(monoRuntimePath);
					else
						this.UpdateRuntimeVersionLabel(monoRuntimeVersion.ToString(), "Mono", isValid: false);
				}
				catch (Exception e)
				{
					if (Settings.Current.Verbose)
						Debug.LogError(e);
					this.UpdateRuntimeVersionLabel(Resources.UI_UNITYPLUGIN_WINDOWRUNTIMEVERSIONERROR, "Mono", isValid: false);
				}
			}
		}

		private void RaiseDone(string monoRuntimePath)
		{
			MonoRuntimeInformation.MonoPath = monoRuntimePath;
			if (this.Done != null)
				this.Done(this, EventArgs.Empty);
			this.Done = null;
			this.Cancel = null;

			if (!this.autoClose)
				return;

			if (Settings.Current.Verbose)
				Debug.Log(string.Format("'{0}' window is closed with selected Mono Runtime path: {1}. Runtime version: {2}.", this.titleContent.text, MonoRuntimeInformation.MonoPath, this.runtimeVersion));
			this.Close();
		}
		private void RaiseCancel()
		{
			if (this.Cancel != null)
			{
				this.Cancel(this, new ErrorEventArgs(new InvalidOperationException(Resources.UI_UNITYPLUGIN_OPERATIONCANCELLED)));
				if (Settings.Current.Verbose)
					Debug.Log(string.Format("'{0}' window is closed by user.", this.titleContent.text));
			}
			this.Cancel = null;
			this.Done = null;
		}
		private void UpdateRuntimeVersionLabel(string version, string runtime, bool isValid)
		{
			if (isValid)
				this.runtimeVersion = string.Format("{0} ({1})", version, runtime);
			else
				this.runtimeVersion = string.Format("<color=#ff0000ff>{0} ({1})</color>", version, runtime);

			this.Repaint();
		}
		private void OnDestroy()
		{
			this.RaiseCancel();
		}

		public static Promise ShowAsync(bool autoClose = true)
		{
			var promise = new Promise();
			var window = GetWindow<UpdateRuntimeWindow>(utility: true);

			window.Done += (sender, args) => promise.TrySetCompleted();
			window.Cancel += (sender, args) => promise.TrySetFailed(args.GetException());

			window.monoPath = string.IsNullOrEmpty(MonoRuntimeInformation.MonoPath) ? MonoRuntimeInformation.MonoDefaultLocation : MonoRuntimeInformation.MonoPath;
			window.autoClose = autoClose;
			if (RuntimeInformation.IsWindows)
				window.runtimeVersion = DotNetRuntimeInformation.GetVersion();

			window.RunCheck();
			window.Focus();

			if (Settings.Current.Verbose)
				Debug.Log(string.Format("Showing '{0}' window. Where current mono location: '{1}'.", window.titleContent.text, window.monoPath));

			return promise;
		}
	}
}
