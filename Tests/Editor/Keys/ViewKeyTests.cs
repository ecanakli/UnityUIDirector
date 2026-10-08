using System;
using NUnit.Framework;

namespace Ecanakli.UIDirector.Tests
{
    // A key id is valid unless it is null, empty or whitespace only; a valid id is kept exactly as given.
    public sealed class ViewKeyTests
    {
        [Test]
        public void Constructor_NullId_ThrowsArgumentNullException()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new ViewKey<NoArgs, NoResult>(null));

            Assert.That(exception.ParamName, Is.EqualTo("id"));
        }

        [TestCase("")]
        [TestCase(" ")]
        [TestCase("\t \n")]
        public void Constructor_EmptyOrWhitespaceOnlyId_ThrowsArgumentException(string id)
        {
            var exception = Assert.Throws<ArgumentException>(() => new ViewKey<NoArgs, NoResult>(id));

            Assert.That(exception.ParamName, Is.EqualTo("id"));
        }

        [Test]
        public void Constructor_IdWithSurroundingSpaces_KeepsTheIdUnchanged()
        {
            var key = new ViewKey<NoArgs, NoResult>(" home ");

            Assert.That(key.Id, Is.EqualTo(" home "), "The id is not trimmed, so it matches the catalog row exactly.");
        }

        [Test]
        public void Create_WhitespaceOnlyId_ThrowsArgumentException()
        {
            var exception = Assert.Throws<ArgumentException>(() => ViewKey.Create(" "));

            Assert.That(exception.ParamName, Is.EqualTo("id"));
        }
    }
}
