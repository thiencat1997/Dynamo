﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Threading;
using System.IO;
using DesignScript.Builtin;
using Dynamo.Models;
using DynamoUtilities;
using Dynamo.Applications;
using Dynamo.Graph;
using Dynamo.Graph.Workspaces;

namespace DynamoCLI
{

    /// <summary>
    /// This class invokes a dynamo model's run methods in a headless mode from the CLI using a set of flags.
    /// This class also has a very limited method for exporting the graph evaluation to an xml file, so that 
    /// the results from invoking dynamo from the command line are useable.
    /// </summary>
    public class CommandLineRunner
    {
        private readonly DynamoModel model;

        public CommandLineRunner(DynamoModel model)
        {
            this.model = model;
        }

        private static XmlDocument RunCommandLineArgs(DynamoModel model, StartupUtils.CommandLineArguments cmdLineArgs)
        {
            var evalComplete = false;
            if (string.IsNullOrEmpty(cmdLineArgs.OpenFilePath))
            {
                return null;
            }
            if (!(string.IsNullOrEmpty(cmdLineArgs.CommandFilePath)))
            {
                Console.WriteLine("commandFilePath option is only available when running DynamoSandbox, not DynamoCLI");
            }

            model.OpenFileFromPath(cmdLineArgs.OpenFilePath, true);
            Console.WriteLine("loaded file");
            model.EvaluationCompleted += (o, args) => { evalComplete = true; };

            // Build a list of states, by default there is only a single state `default`
            // If the desire is to have additional states you can add logic here to build
            // up a list and iterate through each state in the list using the loop below.
            // This must be done after potentially loading states from an external file.
            var stateNames = new List<String>();
            stateNames.Add("default");

            var outputresults = new List<Dictionary<Guid, List<object>>>();
            XmlDocument doc = null;
            foreach (var stateName in stateNames)
            {
                // Graph execution
                model.ExecuteCommand(new DynamoModel.RunCancelCommand(false, false));

                while (evalComplete == false)
                {
                    Thread.Sleep(250);
                }

                //if verbose was true, then print all nodes to the console
                if (!String.IsNullOrEmpty(cmdLineArgs.Verbose))
                {
                    doc = new XmlDocument();
                    var resultsdict = new Dictionary<Guid, List<object>>();
                    foreach (var node in model.CurrentWorkspace.Nodes)
                    {
                        var portvalues = new List<object>();
                        foreach (var port in node.OutPorts)
                        {
                            var value = node.GetValue(port.Index, model.EngineController);
                            if (value.IsCollection)
                            {
                                portvalues.Add(GetStringRepOfCollection(value));
                            }
                            else if (value.IsDictionary)
                            {
                                portvalues.Add(GetStringRepOfDictionary(value.Data));
                            }
                            else
                            {
                                portvalues.Add(value.StringData);
                            }
                        }

                        resultsdict.Add(node.GUID, portvalues);
                    }
                    outputresults.Add(resultsdict);
                    populateXmlDocWithResults(doc, outputresults);
                }
                evalComplete = false;

            }

            return doc;
        }

        private static string GetStringRepOfCollection(ProtoCore.Mirror.MirrorData value)
        {
            var items = string.Join(",",
                value.GetElements().Select(x =>
                {
                    if(x.IsCollection) return GetStringRepOfCollection(x);
                    return x.IsDictionary ? GetStringRepOfDictionary(x.Data) : x.StringData;
                }));
            return "{" + items + "}";
        }

        private static string GetStringRepOfDictionary(object value)
        {
            if (value is DesignScript.Builtin.Dictionary || value is IDictionary)
            {
                IEnumerable<string> keys;
                IEnumerable<object> values;
                var dictionary = value as Dictionary;
                if (dictionary != null)
                {
                    var dict = dictionary;
                    keys = dict.Keys;
                    values = dict.Values;
                }
                else
                {
                    var dict = (IDictionary) value;
                    keys = dict.Keys.Cast<string>();
                    values = dict.Values.Cast<object>();
                }
                var items = string.Join(", ", keys.Zip(values, (str, obj) => str + " : " + GetStringRepOfDictionary(obj)));
                return "{" + items + "}";
            }
            if (!(value is string) && value is IEnumerable)
            {
                var list = ((IEnumerable) value).Cast<dynamic>().ToList();
                var items = string.Join(", ", list.Select(x => GetStringRepOfDictionary(x)));
                return "{" + items + "}";
            }
            return value.ToString();
        }

        private static void populateXmlDocWithResults(XmlDocument doc, List<Dictionary<Guid, List<object>>> resultsDict)
        {
            var evalele = doc.CreateElement("evaluations");
            doc.AppendChild(evalele);
            foreach (var evaluation in resultsDict)
            {
                var index = resultsDict.IndexOf(evaluation);

                var currenteval = doc.CreateElement("evaluation" + index.ToString());
                evalele.AppendChild(currenteval);
                //foreach node:results pair in this eval
                foreach (KeyValuePair<Guid, List<object>> entry in resultsDict[index])
                {
                    var nodeval = doc.CreateElement("Node");
                    nodeval.SetAttribute("guid", entry.Key.ToString());
                    currenteval.AppendChild(nodeval);

                    foreach (var value in entry.Value)
                    {
                        var portindex = entry.Value.IndexOf(value);
                        var portelement = doc.CreateElement("output" + portindex.ToString());
                        portelement.SetAttribute("value", value.ToString());
                        nodeval.AppendChild(portelement);
                    }
                }

            }
        }

        private static void OpenWorkspaceAndConvert(DynamoModel model, string dynPath)
        {
            model.OpenFileFromPath(dynPath);

            var ws = model.CurrentWorkspace;
            var json = ws.ToJson(model.EngineController);

            var newFilePath = Path.Combine(Path.GetDirectoryName(dynPath), Path.GetFileNameWithoutExtension(dynPath) + ".json");
            File.WriteAllText(newFilePath, json);
        }

        public void Run(StartupUtils.CommandLineArguments args)
        {
            if (args.ConvertFile)
            {
                OpenWorkspaceAndConvert(model, args.OpenFilePath);
                return;
            }

            var doc = RunCommandLineArgs(this.model, args);
            if (doc != null && Directory.Exists(new FileInfo(args.Verbose).Directory.FullName))
            {
                //if it exists and the path is valid, save the output file
                doc.Save(args.Verbose);
            }
        }
    }
}
