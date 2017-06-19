﻿using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Shouldly;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Concatenation;
using tusdotnet.Models.Expiration;
using tusdotnet.test.Data;
using tusdotnet.test.Extensions;
using Xunit;

namespace tusdotnet.test.Tests
{
    public class ExpirationTests
    {

        /*
             * Varje patch request innehåller Upload-Expires om expiration är satt
             *  Patch innehåller inte Upload-Expires om expiration inte är satt
             * POST request innehåller Upload-Expires om expiration är satt
             *  Post innehåller inte...
             * Om sliding expiration används så uppdateras file expiration på varje upload request
             * Om absolute expiration används uppdateras den inte
             * Returnerar 404 om expires har passerat
             * Upload-Expires är i format RFC 7231 -> Sun, 06 Nov 1994 08:49:37 GMT
             * */

        [Theory, XHttpMethodOverrideData]
        public async Task Patch_Requests_Contain_Upload_Expires_Header_For_Normal_Uploads_If_Expiration_Is_Configured(string methodToUse)
        {
            var store = Substitute.For<ITusStore, ITusExpirationStore>();
            var expirationStore = (ITusExpirationStore)store;

            store.FileExistAsync(null, CancellationToken.None).ReturnsForAnyArgs(true);
            store.GetUploadLengthAsync(null, CancellationToken.None).ReturnsForAnyArgs(10);
            store.GetUploadOffsetAsync(null, CancellationToken.None).ReturnsForAnyArgs(3);
            expirationStore.GetExpirationAsync(null, CancellationToken.None).ReturnsForAnyArgs(DateTimeOffset.UtcNow.Add(TimeSpan.FromMinutes(8)));

            using (var server = TestServerFactory.Create(app =>
            {
                app.UseTus(request => new DefaultTusConfiguration
                {
                    Store = store,
                    UrlPath = "/files",
                    Expiration = new AbsoluteExpiration(TimeSpan.FromMinutes(8))
                });
            }))
            {
                var response = await server
                    .CreateRequest("/files/expirationtestfile")
                    .And(m => m.AddBody())
                    .AddTusResumableHeader()
                    .AddHeader("Upload-Offset", "3")
                    .OverrideHttpMethodIfNeeded("PATCH", methodToUse)
                    .SendAsync(methodToUse);

                response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
                response.ShouldContainHeader("Upload-Expires", DateTime.UtcNow.Add(TimeSpan.FromMinutes(8)).ToString("r"));
            }
        }

        [Theory, XHttpMethodOverrideData]
        public async Task Patch_Requests_Contain_Upload_Expires_Header_For_Partial_Uploads_If_Expiration_Is_Configured(
            string methodToUse)
        {
            var store = Substitute.For<ITusStore, ITusExpirationStore, ITusConcatenationStore>();
            var expirationStore = (ITusExpirationStore)store;
            var concatStore = (ITusConcatenationStore)store;

            var expires = DateTimeOffset.UtcNow.Add(TimeSpan.FromMinutes(8));

            store.FileExistAsync(null, CancellationToken.None).ReturnsForAnyArgs(true);
            store.GetUploadLengthAsync(null, CancellationToken.None).ReturnsForAnyArgs(10);
            store.GetUploadOffsetAsync(null, CancellationToken.None).ReturnsForAnyArgs(3);
            expirationStore.GetExpirationAsync(null, CancellationToken.None).ReturnsForAnyArgs(expires);
            concatStore.GetUploadConcatAsync(null, CancellationToken.None).ReturnsForAnyArgs(new FileConcatPartial());

            using (var server = TestServerFactory.Create(app =>
            {
                app.UseTus(request => new DefaultTusConfiguration
                {
                    Store = store,
                    UrlPath = "/files",
                    Expiration = new AbsoluteExpiration(TimeSpan.FromMinutes(8))
                });
            }))
            {
                var response = await server
                    .CreateRequest("/files/expirationtestfile")
                    .And(m => m.AddBody())
                    .AddTusResumableHeader()
                    .AddHeader("Upload-Offset", "3")
                    .OverrideHttpMethodIfNeeded("PATCH", methodToUse)
                    .SendAsync(methodToUse);

                response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
                response.ShouldContainHeader("Upload-Expires", expires.ToString("R"));
            }
        }

        [Theory, XHttpMethodOverrideData]
        public async Task Post_Requests_Contain_Upload_Expires_Header_For_Normal_Uploads_If_Expiration_Is_Configured(string methodToUse)
        {
            var store = Substitute.For<ITusStore, ITusCreationStore, ITusExpirationStore>();
            var creationStore = (ITusCreationStore)store;

            store.FileExistAsync(null, CancellationToken.None).ReturnsForAnyArgs(false);
            creationStore.CreateFileAsync(-1, null, CancellationToken.None)
                .ReturnsForAnyArgs(Guid.NewGuid().ToString());

            using (var server = TestServerFactory.Create(app =>
            {
                app.UseTus(request => new DefaultTusConfiguration
                {
                    Store = store,
                    UrlPath = "/files",
                    Expiration = new AbsoluteExpiration(TimeSpan.FromMinutes(10))
                });
            }))
            {
                var response = await server
                    .CreateRequest("/files")
                    .AddTusResumableHeader()
                    .AddHeader("Upload-Length", "1")
                    .OverrideHttpMethodIfNeeded("POST", methodToUse)
                    .SendAsync(methodToUse);

                response.StatusCode.ShouldBe(HttpStatusCode.Created);
                response.ShouldContainHeader("Upload-Expires", DateTime.UtcNow.Add(TimeSpan.FromMinutes(10)).ToString("r"));
            }
        }

        [Theory, XHttpMethodOverrideData]
        public async Task Post_Requests_Contain_Upload_Expires_Header_For_Partial_Uploads_If_Expiration_Is_Configured(
            string methodToUse)
        {
            var store = (ITusStore)Substitute.For(new[]
                {
                    typeof(ITusStore),
                    typeof(ITusCreationStore),
                    typeof(ITusConcatenationStore),
                    typeof(ITusExpirationStore)
                },
                new object[0]
            );
            var concatenationStore = (ITusConcatenationStore)store;

            //store.FileExistAsync(null, CancellationToken.None).ReturnsForAnyArgs(false);
            concatenationStore.CreatePartialFileAsync(-1, null, CancellationToken.None)
                .ReturnsForAnyArgs(Guid.NewGuid().ToString());

            using (var server = TestServerFactory.Create(app =>
            {
                app.UseTus(request => new DefaultTusConfiguration
                {
                    Store = store,
                    UrlPath = "/files",
                    Expiration = new AbsoluteExpiration(TimeSpan.FromMinutes(10))
                });
            }))
            {
                var response = await server
                    .CreateRequest("/files")
                    .AddTusResumableHeader()
                    .AddHeader("Upload-Length", "1")
                    .AddHeader("Upload-Concat", "partial")
                    .OverrideHttpMethodIfNeeded("POST", methodToUse)
                    .SendAsync(methodToUse);

                response.StatusCode.ShouldBe(HttpStatusCode.Created);
                response.ShouldContainHeader("Upload-Expires", DateTime.UtcNow.Add(TimeSpan.FromMinutes(10)).ToString("r"));
            }
        }

        [Theory, XHttpMethodOverrideData]
        public async Task Post_Requests_Does_Not_Contain_Upload_Expires_Header_For_Final_Uploads_If_Expiration_Is_Configured(
            string methodToUse)
        {
            var store = (ITusStore)Substitute.For(new[]
                    {
                    typeof(ITusStore),
                    typeof(ITusCreationStore),
                    typeof(ITusConcatenationStore),
                    typeof(ITusExpirationStore)
                },
                    new object[0]
                );
            var concatenationStore = (ITusConcatenationStore)store;

            concatenationStore.GetUploadConcatAsync("partial1", Arg.Any<CancellationToken>())
                .Returns(new FileConcatPartial());
            concatenationStore.GetUploadConcatAsync("partial2", Arg.Any<CancellationToken>())
                .Returns(new FileConcatPartial());
            store.FileExistAsync("partial1", Arg.Any<CancellationToken>()).Returns(true);
            store.FileExistAsync("partial2", Arg.Any<CancellationToken>()).Returns(true);

            store.GetUploadLengthAsync("partial1", Arg.Any<CancellationToken>()).Returns(10);
            store.GetUploadLengthAsync("partial2", Arg.Any<CancellationToken>()).Returns(10);
            store.GetUploadOffsetAsync("partial1", Arg.Any<CancellationToken>()).Returns(10);
            store.GetUploadOffsetAsync("partial2", Arg.Any<CancellationToken>()).Returns(10);

            concatenationStore.CreateFinalFileAsync(null, null, CancellationToken.None)
                .ReturnsForAnyArgs(Guid.NewGuid().ToString());

            using (var server = TestServerFactory.Create(app =>
            {
                app.UseTus(request => new DefaultTusConfiguration
                {
                    Store = store,
                    UrlPath = "/files",
                    Expiration = new AbsoluteExpiration(TimeSpan.FromMinutes(10))
                });
            }))
            {
                var response = await server
                    .CreateRequest("/files")
                    .AddTusResumableHeader()
                    .AddHeader("Upload-Length", "1")
                    .AddHeader("Upload-Concat", "final;/files/partial1 /files/partial2")
                    .OverrideHttpMethodIfNeeded("POST", methodToUse)
                    .SendAsync(methodToUse);

                response.StatusCode.ShouldBe(HttpStatusCode.Created);
                response.Headers.Contains("Upload-Expires").ShouldBeFalse();
            }
        }

        [Theory, XHttpMethodOverrideData]
        public async Task Upload_Expires_Header_Is_Updated_On_Patch_Requests_If_Sliding_Expiration_Is_Configured(string methodToUse)
        {
            var store = Substitute.For<ITusStore, ITusExpirationStore>();
            var expirationStore = (ITusExpirationStore)store;

            var offset = 3;
            store.FileExistAsync(null, CancellationToken.None).ReturnsForAnyArgs(true);
            store.GetUploadLengthAsync(null, CancellationToken.None).ReturnsForAnyArgs(10);
            store.GetUploadOffsetAsync(null, CancellationToken.None).ReturnsForAnyArgs(ci => offset);
            store.AppendDataAsync("expirationtestfile", Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(ci =>
            {
                offset += 3;
                return offset;
            });

            var expires = DateTimeOffset.MaxValue;

#pragma warning disable 4014
            expirationStore.GetExpirationAsync("expirationtestfile", Arg.Any<CancellationToken>())
                .ReturnsForAnyArgs(ci => expires);
            expirationStore.SetExpirationAsync("expirationtestfile", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(
                ci =>
                {
                    expires = ci.Arg<DateTimeOffset>();
                    return Task.FromResult(0);
                });
#pragma warning restore 4014


            using (var server = TestServerFactory.Create(app =>
            {
                app.UseTus(c => new DefaultTusConfiguration
                {
                    UrlPath = "/files",
                    Store = store,
                    Expiration = new SlidingExpiration(TimeSpan.FromSeconds(5))
                });
            }))
            {

                for (var i = 0; i < 2; i++)
                {
                    var response = await server
                        .CreateRequest("/files/expirationtestfile")
                        .And(m => m.AddBody())
                        .AddTusResumableHeader()
                        .AddHeader("Upload-Offset", offset.ToString())
                        .OverrideHttpMethodIfNeeded("PATCH", methodToUse)
                        .SendAsync(methodToUse);

                    response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
                    response.ShouldContainHeader("Upload-Expires", expires.ToString("R"));
                }

                expirationStore
                    .ReceivedCalls()
                    .Count(f => f.GetMethodInfo().Name == nameof(expirationStore.SetExpirationAsync))
                    .ShouldBe(2);
            }
        }

        [Theory, XHttpMethodOverrideData]
        public async Task Upload_Expires_Header_Is_Not_Updated_On_Patch_Requests_If_Absolute_Expiration_Is_Configured(string methodToUse)
        {
            var store = Substitute.For<ITusStore, ITusExpirationStore>();
            var expirationStore = (ITusExpirationStore)store;

            var offset = 3;
            store.FileExistAsync(null, CancellationToken.None).ReturnsForAnyArgs(true);
            store.GetUploadLengthAsync(null, CancellationToken.None).ReturnsForAnyArgs(10);
            store.GetUploadOffsetAsync(null, CancellationToken.None).ReturnsForAnyArgs(ci => offset);
            store.AppendDataAsync("testexpiration", Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(ci =>
            {
                offset += 3;
                return 3;
            });

            var expires = DateTimeOffset.UtcNow.AddSeconds(5);
#pragma warning disable 4014
            expirationStore.GetExpirationAsync("testexpiration", Arg.Any<CancellationToken>())
                .ReturnsForAnyArgs(expires);
            expirationStore.SetExpirationAsync("testexpiration", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
#pragma warning restore 4014


            using (var server = TestServerFactory.Create(app =>
            {
                app.UseTus(c => new DefaultTusConfiguration
                {
                    UrlPath = "/files",
                    Store = store,
                    Expiration = new AbsoluteExpiration(TimeSpan.FromSeconds(5))
                });
            }))
            {
                var response = await server
                    .CreateRequest("/files/testexpiration")
                    .And(m => m.AddBody())
                    .AddTusResumableHeader()
                    .AddHeader("Upload-Offset", offset.ToString())
                    .OverrideHttpMethodIfNeeded("PATCH", methodToUse)
                    .SendAsync(methodToUse);

                response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
                response.ShouldContainHeader("Upload-Expires", expires.ToString("R"));

                response = await server
                    .CreateRequest("/files/testexpiration")
                    .And(m => m.AddBody())
                    .AddTusResumableHeader()
                    .AddHeader("Upload-Offset", offset.ToString())
                    .OverrideHttpMethodIfNeeded("PATCH", methodToUse)
                    .SendAsync(methodToUse);

                response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
                response.ShouldContainHeader("Upload-Expires", expires.ToString("R"));

                expirationStore
                    .ReceivedCalls()
                    .Count(f => f.GetMethodInfo().Name == nameof(expirationStore.SetExpirationAsync))
                    .ShouldBe(0);
            }
        }

        [Theory, XHttpMethodOverrideData]
        public async Task Requests_Return_404_NotFound_If_Upload_Expires_Header_Has_Expired(string methodToUse)
        {
            var store = Substitute.For<ITusStore, ITusExpirationStore>();
            var expirationStore = (ITusExpirationStore)store;
            store.FileExistAsync(null, CancellationToken.None).ReturnsForAnyArgs(true);
            expirationStore.GetExpirationAsync(null, CancellationToken.None)
                .ReturnsForAnyArgs(DateTimeOffset.UtcNow.AddSeconds(-1));

            using (var server = TestServerFactory.Create(app =>
            {
                app.UseTus(c => new DefaultTusConfiguration
                {
                    UrlPath = "/files",
                    Store = store,
                    Expiration = new AbsoluteExpiration(TimeSpan.FromSeconds(5))
                });
            }))
            {
                foreach (var method in new[] { "PUT", "DELETE", "HEAD" })
                {
                    var response = await server
                        .CreateRequest("/files/expirationtestfile")
                        .And(m => m.AddBody())
                        .AddTusResumableHeader()
                        .OverrideHttpMethodIfNeeded(method, methodToUse)
                        .SendAsync(methodToUse);

                    response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
                }
            }
        }
    }
}