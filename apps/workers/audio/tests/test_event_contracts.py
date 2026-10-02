from jsonschema import Draft202012Validator, FormatChecker


def test_every_versioned_event_schema_has_a_valid_fixture(event_schemas, event_fixtures):
    assert set(event_schemas) == set(event_fixtures)

    for event_name, schema in event_schemas.items():
        Draft202012Validator.check_schema(schema)
        Draft202012Validator(schema, format_checker=FormatChecker()).validate(
            event_fixtures[event_name]
        )


def test_request_event_has_only_canonical_video_and_interval_data(event_fixtures):
    event = event_fixtures["audio-extraction-requested"]

    assert set(event["data"]) == {
        "videoId",
        "startSeconds",
        "endSeconds",
        "outputFormat",
    }
    assert "url" not in event["data"]
