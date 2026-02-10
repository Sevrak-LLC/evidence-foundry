using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class WorldModelGeneratorTests
{
    [Fact]
    public void ParseWorldModelJson_MapsOrganizationsAndKeyPeople()
    {
        var json = """
        {
          "worldModel": {
            "caseContext": {
              "caseArea": "Contract",
              "matterType": "Dispute",
              "issue": "Late Delivery",
              "issueDescription": "Delivery delays tied to vendor scheduling."
            },
            "organizations": {
              "plaintiffs": [
                {
                  "name": "Atlas Works",
                  "domain": "atlasworks.com",
                  "description": "We build project management tools. We focus on enterprise delivery.",
                  "organizationType": "LLC",
                  "industry": "InformationTechnology",
                  "state": "CA",
                  "founded": 2005,
                  "keyPeople": [
                    {
                      "role": "ChiefExecutiveOfficer",
                      "department": "Executive",
                      "firstName": "Maya",
                      "lastName": "Fields",
                      "email": "maya.fields@atlasworks.com",
                      "personality": "Decisive but impatient. Values transparency and speed.",
                      "communicationStyle": "Direct, brief, and action-oriented.",
                      "involvement": "Actor",
                      "involvementSummary": "Approves the vendor schedule changes."
                    }
                  ]
                }
              ],
              "defendants": [
                {
                  "name": "Crescent Logistics",
                  "domain": "crescentlogistics.com",
                  "description": "We manage regional distribution networks. Reliability is our brand.",
                  "organizationType": "LLC",
                  "industry": "TransportationAndLogistics",
                  "state": "TX",
                  "founded": 1998,
                  "keyPeople": [
                    {
                      "role": "ChiefOperatingOfficer",
                      "department": "Executive",
                      "firstName": "Elliot",
                      "lastName": "Parks",
                      "email": "elliot.parks@crescentlogistics.com",
                      "personality": "Methodical and cautious. Avoids conflict but protects margins.",
                      "communicationStyle": "Polite, structured updates with measured tone.",
                      "involvement": "Target",
                      "involvementSummary": "Receives the escalation and responds to schedule demands."
                    }
                  ]
                }
              ]
            }
          }
        }
        """;

        var world = WorldModelGenerator.ParseWorldModelJson(json, 1, 1);

        Assert.Equal("Contract", world.CaseContext.CaseArea);
        Assert.Single(world.Plaintiffs);
        Assert.Single(world.Defendants);

        var plaintiff = world.Plaintiffs[0];
        Assert.Equal("Atlas Works", plaintiff.Name);
        Assert.Equal(OrganizationType.LLC, plaintiff.OrganizationType);
        Assert.Equal(Industry.InformationTechnology, plaintiff.Industry);
        Assert.Single(plaintiff.Departments);

        var keyPerson = world.KeyPeople.Single(p => p.Email == "maya.fields@atlasworks.com");
        Assert.Equal("Maya", keyPerson.FirstName);
        Assert.Equal("Actor", keyPerson.Involvement);
        Assert.Equal("Direct, brief, and action-oriented.", keyPerson.CommunicationStyle);
    }

    [Fact]
    public void ParseWorldModelJson_ThrowsOnInvalidEmailFormat()
    {
        var json = """
        {
          "worldModel": {
            "caseContext": {
              "caseArea": "Contract",
              "matterType": "Dispute",
              "issue": "Late Delivery",
              "issueDescription": "Delivery delays tied to vendor scheduling."
            },
            "organizations": {
              "plaintiffs": [
                {
                  "name": "Atlas Works",
                  "domain": "atlasworks.com",
                  "description": "We build project management tools. We focus on enterprise delivery.",
                  "organizationType": "LLC",
                  "industry": "InformationTechnology",
                  "state": "CA",
                  "founded": 2005,
                  "keyPeople": [
                    {
                      "role": "ChiefExecutiveOfficer",
                      "department": "Executive",
                      "firstName": "Maya",
                      "lastName": "Fields",
                      "email": "m.fields@atlasworks.com",
                      "personality": "Decisive but impatient. Values transparency and speed.",
                      "communicationStyle": "Direct, brief, and action-oriented.",
                      "involvement": "Actor",
                      "involvementSummary": "Approves the vendor schedule changes."
                    }
                  ]
                }
              ],
              "defendants": []
            }
          }
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WorldModelGenerator.ParseWorldModelJson(json, 1, 0));

        Assert.Contains("email must be", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
