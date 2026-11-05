@{
    Severity     = @('Error', 'Warning', 'Information')
    # These are command-line scripts, and their output is meant for the person running them.
    ExcludeRules = @('PSAvoidUsingWriteHost')
}
