﻿#region header
// ========================================================================
// Copyright (c) 2015 - Julien Caillon (julien.caillon@gmail.com)
// This file (ProgressExecution.cs) is part of 3P.
// 
// 3P is a free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// 3P is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with 3P. If not, see <http://www.gnu.org/licenses/>.
// ========================================================================
#endregion

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using _3PA.Data;
using _3PA.Html;
using _3PA.Lib;

namespace _3PA.MainFeatures.ProgressExecution {
    public class ProgressExecution {
        #region public fields

        /// <summary>
        /// Full path to the directory containing all the files needed for the execution
        /// </summary>
        public string ExecutionDir { get; private set; }

        /// <summary>
        /// Full path to the .p, .w... to compile, run..., copied in the temp execution directory
        /// </summary>
        public string FullFilePathToExecute { get; private set; }

        /// <summary>
        /// Path to the output .log file (for compilation)
        /// </summary>
        public string LogPath { get; private set; }

        /// <summary>
        /// Path to the output .lst file (for compilation)
        /// </summary>
        public string LstPath { get; private set; }

        /// <summary>
        /// Path to the output .r file (for compilation)
        /// </summary>
        public string DotRPath { get; private set; }

        /// <summary>
        /// Directory where the file will be moved to after the compilation
        /// </summary>
        public string CompilationDir { get; private set; }

        /// <summary>
        /// progress32.exe used for the execution/compilation
        /// </summary>
        public string ProgressWin32 { get; private set; }

        public ExecutionType ExecutionType { get; private set; }

        public string ExeParameters { get; private set; }

        public Process Process { get; private set; }

        #endregion

        #region private fields

        /// <summary>
        /// path to temp ProgressRun.p
        /// </summary>
        private string _runnerPath;

        /// <summary>
        /// Path to the file to compile/run
        /// </summary>
        private string _tempFullFilePathToExecute;

        private readonly bool _isCurrentFile;

        #endregion

        #region constructors

        /// <summary>
        /// Creates a progress execution environnement, to compile or run a program
        /// </summary>
        /// <param name="tempFullFilePathToExecute"></param>
        public ProgressExecution(string tempFullFilePathToExecute) {
            FullFilePathToExecute = tempFullFilePathToExecute;
            _isCurrentFile = false;
        }

        /// <summary>
        /// Creates a progress execution environnement, to compile or run the current program
        /// </summary>
        public ProgressExecution() {
            FullFilePathToExecute = Npp.GetCurrentFilePath();
            _isCurrentFile = true;
        }

        #endregion

        #region public methods

        /// <summary>
        /// allows to prepare the execution environnement by creating a unique temp folder
        /// and copying every critical files into it
        /// Then execute the progress program
        /// </summary>
        /// <returns></returns>
        public bool Do(ExecutionType executionType) {
            // check info
            if (executionType != ExecutionType.Database) {
                if (_isCurrentFile && !Abl.IsCurrentProgressFile()) {
                    UserCommunication.Notify("Can only compile and run progress files!", MessageImg.MsgWarningShield,
                        "Invalid file type", duration: 10);
                    return false;
                }
                if (string.IsNullOrEmpty(FullFilePathToExecute) || !File.Exists(FullFilePathToExecute)) {
                    UserCommunication.Notify("Couldn't find the following file :<br>" + FullFilePathToExecute,
                        MessageImg.MsgError, "Execution error", duration: 10);
                    return false;
                }
                if (Config.Instance.GlobalCompilableExtension.Split().Contains(Path.GetExtension(FullFilePathToExecute))) {
                    UserCommunication.Notify("Sorry, the file extension " + Path.GetExtension(FullFilePathToExecute).ProgressQuoter() + " isn't a valid extension for this action!<br><i>You can change the list of valid extensions in the settings window</i>", MessageImg.MsgWarningShield, "Invalid file extension", duration: 10);
                    return false;
                }
            }
            if (!File.Exists(ProgressEnv.Current.ProwinPath)) {
                UserCommunication.Notify("The file path to prowin32.exe is incorrect : <br>" + ProgressEnv.Current.ProwinPath + "<br>You must provide a valid path before executing this action<br><i>You can change this path in the settings window</i>", MessageImg.MsgWarningShield, "Invalid file extension", duration: 10);
                return false;
            }

            // create unique execution folder
            ExecutionDir = Path.Combine(Plug.TempDir, DateTime.Now.ToString("yyMMdd_HHmmssfff"));
            while (Directory.Exists(ExecutionDir)) ExecutionDir += "_";
            try {
                Directory.CreateDirectory(ExecutionDir);
            } catch (Exception e) {
                ErrorHandler.ShowErrors(e, "Permission denied when creating " + ExecutionDir);
                ExecutionDir = "";
                return false;
            }

            // Move context files into the execution dir
            if (File.Exists(ProgressEnv.Current.GetCurrentPfPath()))
                File.Copy(ProgressEnv.Current.GetCurrentPfPath(), Path.Combine(ExecutionDir, "base.pf"));

            if (!string.IsNullOrEmpty(ProgressEnv.Current.DataBaseConnection))
                File.WriteAllText(Path.Combine(ExecutionDir, "extra.pf"), ProgressEnv.Current.DataBaseConnection);

            if (File.Exists(ProgressEnv.Current.IniPath))
                File.Copy(ProgressEnv.Current.IniPath, Path.Combine(ExecutionDir, "base.ini"));

            // If current file, copy Npp.Text to a temp file to be executed
            if (executionType != ExecutionType.Database) {
                if (_isCurrentFile) {
                    _tempFullFilePathToExecute = Path.Combine(ExecutionDir, (Path.GetFileName(FullFilePathToExecute) ?? "gg"));
                    File.WriteAllText(_tempFullFilePathToExecute, Npp.Text);
                } else _tempFullFilePathToExecute = FullFilePathToExecute;
            }

            // set info on the execution
            var baseFileName = Path.GetFileNameWithoutExtension(_tempFullFilePathToExecute);
            LogPath = Path.Combine(ExecutionDir, baseFileName + ".log");
            LstPath = Path.Combine(ExecutionDir, baseFileName + ".lst");
            DotRPath = Path.Combine(ExecutionDir, baseFileName + ".r");
            ExecutionType = executionType;

            // prepare the preproc variable of the .p runner
            var programContent = new StringBuilder();
            programContent.AppendLine("&SCOPED-DEFINE ExecutionType " + executionType.ToString().ToUpper().ProgressQuoter());
            programContent.AppendLine("&SCOPED-DEFINE ToCompile " + _tempFullFilePathToExecute.ProgressQuoter());
            programContent.AppendLine("&SCOPED-DEFINE CompilePath " + DotRPath.ProgressQuoter());
            programContent.AppendLine("&SCOPED-DEFINE LogFile " + LogPath.ProgressQuoter());
            programContent.AppendLine("&SCOPED-DEFINE LstFile " + LstPath.ProgressQuoter());
            programContent.AppendLine("&SCOPED-DEFINE ExtractDbOutputPath " + "".ProgressQuoter());
            programContent.AppendLine("&SCOPED-DEFINE propathToUse " + (ExecutionDir + "," + ProgressEnv.Current.ProPath).ProgressQuoter());
            programContent.Append(Encoding.Default.GetString(DataResources.ProgressRun));

            // progress runner
            var runnerFileName = DateTime.Now.ToString("yyMMdd_HHmmssfff") + ".p";
            _runnerPath = Path.Combine(ExecutionDir, runnerFileName);
            File.WriteAllText(_runnerPath, programContent.ToString());

            // misc
            ProgressWin32 = ProgressEnv.Current.ProwinPath;
            CompilationDir = ProgressEnv.Current.BaseCompilationPath; //TODO : compilationPath!

            // Parameters
            StringBuilder Params = new StringBuilder();
            Params.Append(" -cpinternal ISO8859-1");
            Params.Append(" -p " + runnerFileName.ProgressQuoter());

            if (File.Exists(Path.Combine(ExecutionDir, "base.ini")))
                Params.Append(" -ini " + ("base.ini").ProgressQuoter());
            ExeParameters = Params.ToString();

            // Start a process
            // execute
            Process = new Process {
                StartInfo = {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    FileName = ProgressEnv.Current.ProwinPath,
                    Arguments = ExeParameters,
                    WorkingDirectory = ExecutionDir
                },
                EnableRaisingEvents = true
            };
            Process.Exited += ProcessOnExited;
            Process.Start();

            UserCommunication.Notify(ExeParameters);

            return true;
        }

        private void ProcessOnExited(object sender, EventArgs eventArgs) {
            // special case for a database structure extraction
            if (ExecutionType == ExecutionType.Database) {
                // update auto-completion

                return;
            }

            // We just compiled/run the current file
            if (Npp.GetCurrentFilePath().EqualsCi(FullFilePathToExecute)) {
                
            }
            UserCommunication.Notify("Ended : " + LogPath);
        }

        /// <summary>
        /// Deletes temp directory and everything in it
        /// </summary>
        public void CleanUp() {
            if (!Process.HasExited)
                Process.Kill();
            Utils.DeleteDirectory(ExecutionDir, true);
        }

        #endregion
    }

    public enum ExecutionType {
        Compile,
        Run,
        Database
    }
}
