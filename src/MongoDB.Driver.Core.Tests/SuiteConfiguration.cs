﻿/* Copyright 2010-2014 MongoDB Inc.
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
* http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*/

using System;
using System.Linq;
using System.Threading;
using MongoDB.Driver.Core.Bindings;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Configuration;
using MongoDB.Driver.Core.Misc;
using MongoDB.Driver.Core.Operations;
using MongoDB.Driver.Core.Servers;
using MongoDB.Driver.Core.SyncExtensionMethods;
using MongoDB.Driver.Core.WireProtocol.Messages.Encoders;
using NUnit.Framework;

namespace MongoDB.Driver
{
    [SetUpFixture]
    public class SuiteConfiguration
    {
        #region static
        // static fields
        private static Lazy<ICluster> __cluster = new Lazy<ICluster>(CreateCluster, isThreadSafe: true);
        private static ConnectionString __connectionString;
        private static DatabaseNamespace __databaseNamespace;
        private static MessageEncoderSettings __messageEncoderSettings = new MessageEncoderSettings();

        // static properties
        public static ICluster Cluster
        {
            get { return __cluster.Value; }
        }

        public static ConnectionString ConnectionString
        {
            get { return __connectionString; }
        }

        public static DatabaseNamespace DatabaseNamespace
        {
            get { return __databaseNamespace; }
        }

        public static MessageEncoderSettings MessageEncoderSettings
        {
            get { return __messageEncoderSettings; }
        }

        public static SemanticVersion ServerVersion
        {
            get
            {
                var writableServerDescription = __cluster.Value.Description.Servers.FirstOrDefault(
                    description => description.Type.IsWritable());
                return writableServerDescription.Version;
            }
        }

        // static methods
        public static ClusterBuilder ConfigureCluster()
        {
            var builder = new ClusterBuilder().ConfigureWithConnectionString(__connectionString);

            if (__connectionString.Ssl.HasValue && __connectionString.Ssl.Value)
            {
                var certificateFilename = Environment.GetEnvironmentVariable("MONGO_SSL_CERT_FILE");
                if (certificateFilename != null)
                {
                    // TODO: configure SSL
                    //builder.ConfigureSsl(ssl =>
                    //{
                    //    var password = Environment.GetEnvironmentVariable("MONGO_SSL_CERT_PASS");
                    //    X509Certificate cert;
                    //    if (password == null)
                    //    {
                    //        cert = new X509Certificate2(certificateFilename);
                    //    }
                    //    else
                    //    {
                    //        cert = new X509Certificate2(certificateFilename, password);
                    //    }
                    //    ssl.AddClientCertificate(cert);
                    //});
                }
            }

            return builder;
        }

        public static ICluster CreateCluster()
        {
            var hasWritableServer = false;
            var builder = ConfigureCluster();
            var cluster = builder.BuildCluster();
            cluster.DescriptionChanged += (o, e) =>
            {
                hasWritableServer = e.NewClusterDescription.Servers.Any(
                    description => description.Type.IsWritable());
            };
            cluster.Initialize();

            // wait until the cluster has connected to a writable server
            SpinWait.SpinUntil(() => hasWritableServer, TimeSpan.FromSeconds(30));
            if (!hasWritableServer)
            {
                throw new Exception("Test cluster has no writable server.");
            }

            return cluster;
        }

        public static CollectionNamespace GetEmptyCollectionForTestFixture(Type testFixtureType)
        {
            var collectionName = testFixtureType.Name;
            var collectionNamespace = new CollectionNamespace(__databaseNamespace, collectionName);

            var operation = new DropCollectionOperation(collectionNamespace, __messageEncoderSettings);
            using (var binding = GetReadWriteBinding())
            {
                operation.Execute(binding);
            }

            return collectionNamespace;
        }

        public static IReadBinding GetReadBinding()
        {
            return GetReadBinding(ReadPreference.Primary);
        }

        public static IReadBinding GetReadBinding(ReadPreference readPreference)
        {
            return new ReadPreferenceBinding(__cluster.Value, readPreference);
        }

        public static IReadWriteBinding GetReadWriteBinding()
        {
            return new WritableServerBinding(__cluster.Value);
        }
        #endregion

        // methods
        private void DropDatabase()
        {
            var operation = new DropDatabaseOperation(__databaseNamespace, __messageEncoderSettings);

            using (var binding = GetReadWriteBinding())
            {
                operation.Execute(binding);
            }
        }

        private ConnectionString GetConnectionString()
        {
            return new ConnectionString(Environment.GetEnvironmentVariable("MONGO_URI") ?? "mongodb://localhost");
        }

        private DatabaseNamespace GetDatabaseNamespace()
        {
            var timestamp = DateTime.Now.ToString("MMddHHmm");
            return new DatabaseNamespace("CoreTests" + timestamp);
        }

        [SetUp]
        public void SuiteConfigurationSetUp()
        {
            __connectionString = GetConnectionString();
            __databaseNamespace = GetDatabaseNamespace();
        }

        [TearDown]
        public void SuiteConfigurationTearDown()
        {
            if (__cluster.IsValueCreated)
            {
                DropDatabase();
                __cluster.Value.Dispose();
            }
        }
    }
}
