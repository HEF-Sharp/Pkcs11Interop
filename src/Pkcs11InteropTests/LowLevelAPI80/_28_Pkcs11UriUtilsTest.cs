﻿/*
 *  Pkcs11Interop - Managed .NET wrapper for unmanaged PKCS#11 libraries
 *  Copyright (c) 2012-2015 JWC s.r.o. <http://www.jwc.sk>
 *  Author: Jaroslav Imrich <jimrich@jimrich.sk>
 *
 *  Licensing for open source projects:
 *  Pkcs11Interop is available under the terms of the GNU Affero General 
 *  Public License version 3 as published by the Free Software Foundation.
 *  Please see <http://www.gnu.org/licenses/agpl-3.0.html> for more details.
 *
 *  Licensing for other types of projects:
 *  Pkcs11Interop is available under the terms of flexible commercial license.
 *  Please contact JWC s.r.o. at <info@pkcs11interop.net> for more details.
 */

using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.LowLevelAPI80;
using NUnit.Framework;
using System;

namespace Net.Pkcs11Interop.Tests.LowLevelAPI80
{
    /// <summary>
    /// Pkcs11UriUtils tests
    /// </summary>
    [TestFixture()]
    public partial class _28_Pkcs11UriUtilsTest
    {
        /// <summary>
        /// Demonstration of PKCS#11 URI usage in a signature creation application
        /// </summary>
        [Test()]
        public void _01_Pkcs11UriInSignatureCreationApplication()
        {
            if (Platform.UnmanagedLongSize != 8 || Platform.StructPackingSize != 0)
                Assert.Inconclusive("Test cannot be executed on this platform");

            // PKCS#11 URI can be acquired i.e. from configuration file as a simple string...
            string uri = @"<pkcs11:serial=7BFF2737350B262C;
                            type=private;
                            object=John%20Doe
                            ?module-path=pkcs11.dll&
                            pin-value=11111111>";

            Assert.IsNotNull(uri);

            // ...or it can be easily constructed with Pkcs11UriBuilder
            Pkcs11UriBuilder pkcs11UriBuilder = new Pkcs11UriBuilder();
            pkcs11UriBuilder.Serial = "7BFF2737350B262C";
            pkcs11UriBuilder.Type = CKO.CKO_PRIVATE_KEY;
            pkcs11UriBuilder.Object = "John Doe";
            pkcs11UriBuilder.ModulePath = "pkcs11.dll";
            pkcs11UriBuilder.PinValue = "11111111";
            uri = pkcs11UriBuilder.ToString();

            Assert.IsNotNull(uri);

            // Warning: Please note that PIN stored in PKCS#11 URI can pose a security risk and therefore other options
            //          should be carefully considered. For example an application may ask for a PIN with a GUI dialog etc.

            // Use PKCS#11 URI acquired from Settings class to identify private key in signature creation method
            byte[] signature = SignData(ConvertUtils.Utf8StringToBytes("Hello world"), Settings.PrivateKeyUri);

            // Do something interesting with the signature
            Assert.IsNotNull(signature);
        }

        /// <summary>
        /// Creates the PKCS#1 v1.5 RSA signature with SHA-1 mechanism
        /// </summary>
        /// <param name="data">Data that should be signed</param>
        /// <param name="uri">PKCS#11 URI identifying PKCS#11 library, token and private key</param>
        /// <returns>PKCS#1 v1.5 RSA signature</returns>
        private byte[] SignData(byte[] data, string uri)
        {
            // Verify input parameters
            if (data == null)
                throw new ArgumentNullException("data");

            if (string.IsNullOrEmpty(uri))
                throw new ArgumentNullException("uri");

            // Parse PKCS#11 URI
            Pkcs11Uri pkcs11Uri = new Pkcs11Uri(uri);

            // Verify that URI contains all information required to perform this operation
            if (pkcs11Uri.ModulePath == null)
                throw new Exception("PKCS#11 URI does not specify PKCS#11 library");

            if (pkcs11Uri.PinValue == null)
                throw new Exception("PKCS#11 URI does not specify PIN");

            if (!pkcs11Uri.DefinesObject || pkcs11Uri.Type != CKO.CKO_PRIVATE_KEY)
                throw new Exception("PKCS#11 URI does not specify private key");

            // Load and initialize PKCS#11 library specified by URI
            CKR rv = CKR.CKR_OK;

            using (Pkcs11 pkcs11 = new Pkcs11(pkcs11Uri.ModulePath, true))
            {
                rv = pkcs11.C_Initialize(Settings.InitArgs80);
                if ((rv != CKR.CKR_OK) && (rv != CKR.CKR_CRYPTOKI_ALREADY_INITIALIZED))
                    Assert.Fail(rv.ToString());

                // Obtain a list of all slots with tokens that match URI
                ulong[] slots = null;
                
                rv = Pkcs11UriUtils.GetMatchingSlotList(pkcs11Uri, pkcs11, true, out slots);
                if (rv != CKR.CKR_OK)
                    Assert.Fail(rv.ToString());

                if ((slots == null) || (slots.Length == 0))
                    throw new Exception("None of the slots matches PKCS#11 URI");

                // Open read only session with first token that matches URI
                ulong session = CK.CK_INVALID_HANDLE;

                rv = pkcs11.C_OpenSession(slots[0], (CKF.CKF_SERIAL_SESSION | CKF.CKF_RW_SESSION), IntPtr.Zero, IntPtr.Zero, ref session);
                if (rv != CKR.CKR_OK)
                    Assert.Fail(rv.ToString());

                // Login as normal user with PIN acquired from URI
                byte[] pinValue = ConvertUtils.Utf8StringToBytes(pkcs11Uri.PinValue);

                rv = pkcs11.C_Login(session, CKU.CKU_USER, pinValue, Convert.ToUInt64(pinValue.Length));
                if (rv != CKR.CKR_OK)
                    Assert.Fail(rv.ToString());

                // Get list of object attributes for the private key specified by URI
                CK_ATTRIBUTE[] attributes = null;

                Pkcs11UriUtils.GetObjectAttributes(pkcs11Uri, out attributes);

                // Find private key specified by URI
                ulong foundObjectCount = 0;
                ulong[] foundObjectIds = new ulong[] { CK.CK_INVALID_HANDLE };

                rv = pkcs11.C_FindObjectsInit(session, attributes, Convert.ToUInt64(attributes.Length));
                if (rv != CKR.CKR_OK)
                    Assert.Fail(rv.ToString());

                rv = pkcs11.C_FindObjects(session, foundObjectIds, Convert.ToUInt64(foundObjectIds.Length), ref foundObjectCount);
                if (rv != CKR.CKR_OK)
                    Assert.Fail(rv.ToString());

                rv = pkcs11.C_FindObjectsFinal(session);
                if (rv != CKR.CKR_OK)
                    Assert.Fail(rv.ToString());

                if ((foundObjectCount == 0) || (foundObjectIds[0] == CK.CK_INVALID_HANDLE))
                    throw new Exception("None of the private keys match PKCS#11 URI");

                // Create signature with the private key specified by URI
                CK_MECHANISM mechanism = CkmUtils.CreateMechanism(CKM.CKM_SHA1_RSA_PKCS);

                rv = pkcs11.C_SignInit(session, ref mechanism, foundObjectIds[0]);
                if (rv != CKR.CKR_OK)
                    Assert.Fail(rv.ToString());

                ulong signatureLen = 0;

                rv = pkcs11.C_Sign(session, data, Convert.ToUInt64(data.Length), null, ref signatureLen);
                if (rv != CKR.CKR_OK)
                    Assert.Fail(rv.ToString());

                Assert.IsTrue(signatureLen > 0);

                byte[] signature = new byte[signatureLen];

                rv = pkcs11.C_Sign(session, data, Convert.ToUInt64(data.Length), signature, ref signatureLen);
                if (rv != CKR.CKR_OK)
                    Assert.Fail(rv.ToString());

                if (signature.Length != Convert.ToInt32(signatureLen))
                    Array.Resize(ref signature, Convert.ToInt32(signatureLen));

                // Release PKCS#11 resources
                rv = pkcs11.C_Logout(session);
                if (rv != CKR.CKR_OK)
                    Assert.Fail(rv.ToString());

                rv = pkcs11.C_CloseSession(session);
                if (rv != CKR.CKR_OK)
                    Assert.Fail(rv.ToString());

                rv = pkcs11.C_Finalize(IntPtr.Zero);
                if (rv != CKR.CKR_OK)
                    Assert.Fail(rv.ToString());

                return signature;
            }
        }
    }
}
