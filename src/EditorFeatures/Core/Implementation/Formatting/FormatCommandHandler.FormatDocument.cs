﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Editor.Implementation.CodeCleanup;
using Microsoft.CodeAnalysis.Editor.Shared.Options;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.CodeAnalysis.Extensions;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Roslyn.Utilities;
using VSCommanding = Microsoft.VisualStudio.Commanding;

namespace Microsoft.CodeAnalysis.Editor.Implementation.Formatting
{
    internal partial class FormatCommandHandler : ForegroundThreadAffinitizedObject
    {
        private bool _infoBarOpen = false;
        public VSCommanding.CommandState GetCommandState(FormatDocumentCommandArgs args)
        {
            return GetCommandState(args.SubjectBuffer);
        }

        private void ShowGoldBarForCodeCleanupConfiguration(Workspace workspace)
        {
            AssertIsForeground();

            // If the gold bar is already open, do not show
            if (_infoBarOpen)
            {
                return;
            }

            _infoBarOpen = true;

            var optionPageService = workspace.Services.GetRequiredService<IOptionPageService>();
            var infoBarService = workspace.Services.GetRequiredService<IInfoBarService>();
            infoBarService.ShowInfoBarInGlobalView(
                EditorFeaturesResources.Code_cleanup_is_not_configured,
                new InfoBarUI(EditorFeaturesResources.Configure_it_now,
                              kind: InfoBarUI.UIKind.Button,
                               () =>
                               {
                                   optionPageService.ShowFormattingOptionPage();
                                   _infoBarOpen = false;
                               }),
                new InfoBarUI(EditorFeaturesResources.Donot_show_this_message_again,
                              kind: InfoBarUI.UIKind.Button,
                              () => { }));
        }

        public bool ExecuteCommand(FormatDocumentCommandArgs args, CommandExecutionContext context)
        {
            if (!args.SubjectBuffer.CanApplyChangeDocumentToWorkspace())
            {
                return false;
            }

            var document = args.SubjectBuffer.CurrentSnapshot.GetOpenDocumentInCurrentContextWithChanges();
            if (document == null)
            {
                return false;
            }

            using (context.WaitContext.AddScope(allowCancellation: true, EditorFeaturesResources.Formatting_document))
            {
                var cancellationToken = context.WaitContext.UserCancellationToken;

                var docOptions = document.GetOptionsAsync(cancellationToken).WaitAndGetResult(cancellationToken);

                using (var transaction = new CaretPreservingEditTransaction(
                    EditorFeaturesResources.Formatting, args.TextView, _undoHistoryRegistry, _editorOperationsFactoryService))
                {
                    if (!docOptions.GetOption(FeatureOnOffOptions.IsCodeCleanupRulesConfigured))
                    {
                        ShowGoldBarForCodeCleanupConfiguration(document.Project.Solution.Workspace);

                        // format
                        var formatChanges = GetFormatChangesAsync(args.TextView, document, selectionOpt: null, cancellationToken).WaitAndGetResult(cancellationToken);
                        if (formatChanges != null && formatChanges.Count > 0)
                        {
                            ApplyChanges(document, formatChanges, selectionOpt: null, cancellationToken);
                        }
                    }
                    else
                    {
                        // Code cleanup
                        var oldDoc = document;
                        var codeCleanupChanges = GetCodeCleanupAndFormatChangesAsync(document, cancellationToken).WaitAndGetResult(cancellationToken);

                        if (codeCleanupChanges != null && codeCleanupChanges.Count() > 0)
                        {
                            ApplyChanges(oldDoc, codeCleanupChanges.ToList(), selectionOpt: null, cancellationToken);
                        }
                    }

                    transaction.Complete();
                }
            }

            return true;
        }

        private async Task<IEnumerable<TextChange>> GetCodeCleanupAndFormatChangesAsync(Document document, CancellationToken cancellationToken)
        {
            var codeCleanupService = document.GetLanguageService<ICodeCleanupService>();
            if (codeCleanupService == null)
            {
                return null;
            }

            var newDoc = await codeCleanupService.CleanupAndFormatDocumentAsync(document, cancellationToken).ConfigureAwait(false);

            return await newDoc.GetTextChangesAsync(document, cancellationToken).ConfigureAwait(false);
        }
    }
}
