﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NSubstitute;
using Octokit.Internal;
using Octokit.Tests.Helpers;
using Xunit;

namespace Octokit.Tests.Clients
{
    /// <summary>
    /// Client tests mostly just need to make sure they call the IApiConnection with the correct 
    /// relative Uri. No need to fake up the response. All *those* tests are in ApiConnectionTests.cs.
    /// </summary>
    public class AuthorizationsClientTests
    {
        public class TheConstructor
        {
            [Fact]
            public void ThrowsForBadArgs()
            {
                Assert.Throws<ArgumentNullException>(() => new AuthorizationsClient(null));
            }
        }

        public class TheGetAllMethod
        {
            [Fact]
            public void GetsAListOfAuthorizations()
            {
                var client = Substitute.For<IApiConnection>();
                var authEndpoint = new AuthorizationsClient(client);

                authEndpoint.GetAll();

                client.Received().GetAll<Authorization>(
                    Arg.Is<Uri>(u => u.ToString() == "authorizations"),
                    null,
                    Arg.Any<string>());
            }
        }

        public class TheGetMethod
        {
            [Fact]
            public void GetsAnAuthorization()
            {
                var client = Substitute.For<IApiConnection>();
                var authEndpoint = new AuthorizationsClient(client);

                authEndpoint.Get(1);

                client.Received().Get<Authorization>(
                    Arg.Is<Uri>(u => u.ToString() == "authorizations/1"),
                    null,
                    Arg.Any<string>());
            }
        }

        public class TheUpdateMethod
        {
            [Fact]
            public void SendsUpdateToCorrectUrl()
            {
                var client = Substitute.For<IApiConnection>();
                var authEndpoint = new AuthorizationsClient(client);

                authEndpoint.Update(1, new AuthorizationUpdate());

                client.Received().Patch<Authorization>(Arg.Is<Uri>(u => u.ToString() == "authorizations/1"),
                    Args.AuthorizationUpdate);
            }
        }

        public class TheDeleteMethod
        {
            [Fact]
            public void DeletesCorrectUrl()
            {
                var client = Substitute.For<IApiConnection>();
                var authEndpoint = new AuthorizationsClient(client);

                authEndpoint.Delete(1);

                client.Received().Delete(Arg.Is<Uri>(u => u.ToString() == "authorizations/1"));
            }
        }

        public class TheGetOrCreateApplicationAuthenticationMethod
        {
            [Fact]
            public void GetsOrCreatesAuthenticationAtCorrectUrl()
            {
                var data = new NewAuthorization();
                var client = Substitute.For<IApiConnection>();
                var authEndpoint = new AuthorizationsClient(client);

                authEndpoint.GetOrCreateApplicationAuthentication("clientId", "secret", data);

                client.Received().Put<ApplicationAuthorization>(Arg.Is<Uri>(u => u.ToString() == "authorizations/clients/clientId"),
                    Args.Object);
            }

            [Fact]
            public void GetsOrCreatesAuthenticationAtCorrectUrlUsingTwoFactor()
            {
                var data = new NewAuthorization();
                var client = Substitute.For<IApiConnection>();
                var authEndpoint = new AuthorizationsClient(client);

                authEndpoint.GetOrCreateApplicationAuthentication("clientId", "secret", data, "two-factor");

                client.Received().Put<ApplicationAuthorization>(
                    Arg.Is<Uri>(u => u.ToString() == "authorizations/clients/clientId"),
                    Args.Object,
                    "two-factor");
            }

            [Fact]
            public async Task WrapsTwoFactorFailureWithTwoFactorException()
            {
                var data = new NewAuthorization();
                var client = Substitute.For<IApiConnection>();
                client.Put<ApplicationAuthorization>(Args.Uri, Args.Object, Args.String)
                    .ThrowsAsync<ApplicationAuthorization>(
                    new AuthorizationException(
                        new Response(HttpStatusCode.Unauthorized , null, new Dictionary<string, string>(), "application/json")));
                var authEndpoint = new AuthorizationsClient(client);

                await AssertEx.Throws<TwoFactorChallengeFailedException>(async () =>
                    await authEndpoint.GetOrCreateApplicationAuthentication("clientId", "secret", data, "authenticationCode"));
            }

            [Fact]
            public async Task UsesCallbackToRetrieveTwoFactorCode()
            {
                var twoFactorChallengeResult = new TwoFactorChallengeResult("two-factor-code");
                var data = new NewAuthorization { Note = "note" };
                var client = Substitute.For<IAuthorizationsClient>();
                client.GetOrCreateApplicationAuthentication("clientId", "secret", Arg.Any<NewAuthorization>())
                    .Returns(_ => {throw new TwoFactorRequiredException();});
                client.GetOrCreateApplicationAuthentication("clientId",
                    "secret",
                    Arg.Any<NewAuthorization>(),
                    "two-factor-code")
                    .Returns(Task.Factory.StartNew(() => new ApplicationAuthorization("xyz")));

                var result = await client.GetOrCreateApplicationAuthentication("clientId",
                    "secret",
                    data,
                    e => Task.Factory.StartNew(() => twoFactorChallengeResult));

                client.Received().GetOrCreateApplicationAuthentication("clientId",
                    "secret",
                    Arg.Is<NewAuthorization>(u => u.Note == "note"));
                client.Received().GetOrCreateApplicationAuthentication("clientId",
                    "secret",
                    Arg.Any<NewAuthorization>(), "two-factor-code");
                Assert.Equal("xyz", result.Token);
            }

            [Fact]
            public async Task RetriesWhenResendRequested()
            {
                var challengeResults = new Queue<TwoFactorChallengeResult>(new []
                {
                    TwoFactorChallengeResult.RequestResendCode,
                    new TwoFactorChallengeResult("two-factor-code")
                });
                var data = new NewAuthorization();
                var client = Substitute.For<IAuthorizationsClient>();
                client.GetOrCreateApplicationAuthentication("clientId", "secret", Arg.Any<NewAuthorization>())
                    .Returns(_ => { throw new TwoFactorRequiredException(); });
                client.GetOrCreateApplicationAuthentication("clientId",
                    "secret",
                    Arg.Any<NewAuthorization>(),
                    "two-factor-code")
                    .Returns(Task.Factory.StartNew(() => new ApplicationAuthorization("OAUTHSECRET")));

                var result = await client.GetOrCreateApplicationAuthentication("clientId",
                    "secret",
                    data,
                    e => Task.Factory.StartNew(() => challengeResults.Dequeue()));

                client.Received(2).GetOrCreateApplicationAuthentication("clientId",
                    "secret",
                    Args.NewAuthorization);
                client.Received().GetOrCreateApplicationAuthentication("clientId",
                    "secret",
                    Args.NewAuthorization,
                    "two-factor-code");
                Assert.Equal("OAUTHSECRET", result.Token);
            }

            [Fact]
            public async Task ThrowsTwoFactorChallengeFailedExceptionWhenProvidedCodeIsIncorrect()
            {
                var challengeResults = new Queue<TwoFactorChallengeResult>(new[]
                {
                    TwoFactorChallengeResult.RequestResendCode,
                    new TwoFactorChallengeResult("wrong-code") 
                });
                var data = new NewAuthorization();
                var client = Substitute.For<IAuthorizationsClient>();
                client.GetOrCreateApplicationAuthentication("clientId", "secret", Arg.Any<NewAuthorization>())
                    .Returns(_ => { throw new TwoFactorRequiredException(); });
                client.GetOrCreateApplicationAuthentication("clientId",
                    "secret",
                    Arg.Any<NewAuthorization>(),
                    "wrong-code")
                    .Returns(_ => { throw new TwoFactorChallengeFailedException(); });
                
                var exception = await AssertEx.Throws<TwoFactorChallengeFailedException>(() =>
                    client.GetOrCreateApplicationAuthentication(
                        "clientId",
                        "secret",
                        data,
                        e => Task.Factory.StartNew(() => challengeResults.Dequeue())));

                Assert.NotNull(exception);
                client.Received().GetOrCreateApplicationAuthentication("clientId",
                    "secret",
                    Arg.Any<NewAuthorization>());
                client.Received().GetOrCreateApplicationAuthentication("clientId",
                    "secret",
                    Arg.Any<NewAuthorization>(),
                    "wrong-code");
            }

            [Fact]
            public async Task GetsOrCreatesAuthenticationWithFingerprintAtCorrectUrl()
            {
                var data = new NewAuthorization { Fingerprint = "ha-ha-fingerprint"};
                var client = Substitute.For<IApiConnection>();
                var authEndpoint = new AuthorizationsClient(client);

                authEndpoint.GetOrCreateApplicationAuthentication("clientId", "secret", data);

                client.Received().Put<ApplicationAuthorization>(Arg.Is<Uri>(u => u.ToString() == "authorizations/clients/clientId/ha-ha-fingerprint"),
                    Args.Object,
                    Args.String,
                    Args.String); // NOTE: preview API
            }
        }

        public class TheCheckApplicationAuthenticationMethod
        {
            [Fact]
            public async Task ChecksApplicationAuthenticateAtCorrectUrl()
            {
                var client = Substitute.For<IApiConnection>();
                var authEndpoint = new AuthorizationsClient(client);

                authEndpoint.CheckApplicationAuthentication("clientId", "accessToken");

                client.Received().Get<ApplicationAuthorization>(
                    Arg.Is<Uri>(u => u.ToString() == "applications/clientId/tokens/accessToken"),
                    null,
                    Arg.Any<string>());
           }

            [Fact]
            public async Task EnsuresArgumentsNotNull()
            {
                var client = Substitute.For<IApiConnection>();
                var authEndpoint = new AuthorizationsClient(client);

                await AssertEx.Throws<ArgumentNullException>(async () => await authEndpoint.CheckApplicationAuthentication(null, "accessToken"));
                await AssertEx.Throws<ArgumentException>(async () => await authEndpoint.CheckApplicationAuthentication("", "accessToken"));
                await AssertEx.Throws<ArgumentNullException>(async () => await authEndpoint.CheckApplicationAuthentication("clientId", null));
                await AssertEx.Throws<ArgumentException>(async () => await authEndpoint.CheckApplicationAuthentication("clientId", ""));
            }
        }

        public class TheResetApplicationAuthenticationMethod
        {
            [Fact]
            public async Task ResetsApplicationAuthenticationAtCorrectUrl()
            {
                var client = Substitute.For<IApiConnection>();
                var authEndpoint = new AuthorizationsClient(client);

                authEndpoint.ResetApplicationAuthentication("clientId", "accessToken");

                client.Received().Post<ApplicationAuthorization>(
                    Arg.Is<Uri>(u => u.ToString() == "applications/clientId/tokens/accessToken"),
                    Args.Object);
            }

            [Fact]
            public async Task EnsuresArgumentsNotNull()
            {
                var client = Substitute.For<IApiConnection>();
                var authEndpoint = new AuthorizationsClient(client);

                await AssertEx.Throws<ArgumentNullException>(async () => await authEndpoint.ResetApplicationAuthentication(null, "accessToken"));
                await AssertEx.Throws<ArgumentException>(async () => await authEndpoint.ResetApplicationAuthentication("", "accessToken"));
                await AssertEx.Throws<ArgumentNullException>(async () => await authEndpoint.ResetApplicationAuthentication("clientId", null));
                await AssertEx.Throws<ArgumentException>(async () => await authEndpoint.ResetApplicationAuthentication("clientId", ""));
            }
        }

        public class TheRevokeApplicationAuthenticationMethod
        {
            [Fact]
            public async Task RevokesApplicatonAuthenticationAtCorrectUrl()
            {
                var client = Substitute.For<IApiConnection>();
                var authEndpoint = new AuthorizationsClient(client);

                authEndpoint.RevokeApplicationAuthentication("clientId", "accessToken");

                client.Received().Delete(
                    Arg.Is<Uri>(u => u.ToString() == "applications/clientId/tokens/accessToken"));
            }

            [Fact]
            public async Task EnsuresArgumentsNotNull()
            {
                var client = Substitute.For<IApiConnection>();
                var authEndpoint = new AuthorizationsClient(client);

                await AssertEx.Throws<ArgumentNullException>(async () => await authEndpoint.RevokeApplicationAuthentication(null, "accessToken"));
                await AssertEx.Throws<ArgumentException>(async () => await authEndpoint.RevokeApplicationAuthentication("", "accessToken"));
                await AssertEx.Throws<ArgumentNullException>(async () => await authEndpoint.RevokeApplicationAuthentication("clientId", null));
                await AssertEx.Throws<ArgumentException>(async () => await authEndpoint.RevokeApplicationAuthentication("clientId", ""));
            }
        }

        public class TheRevokeAllApplicationAuthenticationsMethod
        {
            [Fact]
            public async Task RevokesAllApplicationAuthenticationsAtCorrectUrl()
            {
                var client = Substitute.For<IApiConnection>();
                var authEndpoint = new AuthorizationsClient(client);

                authEndpoint.RevokeAllApplicationAuthentications("clientId");

                client.Received().Delete(
                    Arg.Is<Uri>(u => u.ToString() == "applications/clientId/tokens"));
            }

            [Fact]
            public async Task EnsuresArgumentsNotNull()
            {
                var client = Substitute.For<IApiConnection>();
                var authEndpoint = new AuthorizationsClient(client);

                await AssertEx.Throws<ArgumentNullException>(async () => await authEndpoint.RevokeAllApplicationAuthentications(null));
                await AssertEx.Throws<ArgumentException>(async () => await authEndpoint.RevokeAllApplicationAuthentications(""));
            }
        }
    }
}
