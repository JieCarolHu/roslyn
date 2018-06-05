﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeCleanup;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.CodeAnalysis.Extensions;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Commanding;
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

        private void ShowGoldBarForCodeCleanupConfiguration(Document document)
        {
            AssertIsForeground();

            // If the gold bar is already open, do not show
            if (_infoBarOpen)
            {
                return;
            }

            _infoBarOpen = true;

            var optionPageService = document.GetLanguageService<IOptionPageService>();
            var infoBarService = document.Project.Solution.Workspace.Services.GetRequiredService<IInfoBarService>();
            infoBarService.ShowInfoBarInGlobalView(
                EditorFeaturesResources.Code_cleanup_is_not_configured,
                new InfoBarUI(EditorFeaturesResources.Configure_it_now,
                              kind: InfoBarUI.UIKind.Button,
                              () =>
                              {
                                  optionPageService.ShowFormattingOptionPage();
                                  _infoBarOpen = false;
                              }),
                new InfoBarUI(EditorFeaturesResources.Do_not_show_this_message_again,
                              kind: InfoBarUI.UIKind.Button,
                              () => { })); // no action needed. _infoBarOpen remains true so the infoBar will not shown again.
        }

        public bool ExecuteCommand(FormatDocumentCommandArgs args, CommandExecutionContext context)
        {
            if (!CanExecuteCommand(args.SubjectBuffer))
            {
                return false;
            }

            var document = args.SubjectBuffer.CurrentSnapshot.GetOpenDocumentInCurrentContextWithChanges();
            if (document == null)
            {
                return false;
            }

            using (context.OperationContext.AddScope(allowCancellation: true, EditorFeaturesResources.Formatting_document))
            {
                var cancellationToken = context.OperationContext.UserCancellationToken;

                var docOptions = document.GetOptionsAsync(cancellationToken).WaitAndGetResult(cancellationToken);

                using (var transaction = new CaretPreservingEditTransaction(
                    EditorFeaturesResources.Formatting, args.TextView, _undoHistoryRegistry, _editorOperationsFactoryService))
                {
                    var codeCleanupService = document.GetLanguageService<ICodeCleanupService>();

                    if (codeCleanupService == null)
                    {
                        Format(args.TextView, document, selectionOpt: null, cancellationToken);
                    }
                    else if (!docOptions.GetOption(CodeCleanupOptions.AreCodeCleanupRulesConfigured))
                    {
                        ShowGoldBarForCodeCleanupConfiguration(document);
                        Format(args.TextView, document, selectionOpt: null, cancellationToken);
                    }
                    else
                    {
                        // Code cleanup
                        var oldDoc = document;
                        var codeCleanupChanges = GetCodeCleanupAndFormatChangesAsync(document, codeCleanupService, cancellationToken).WaitAndGetResult(cancellationToken);

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

        private async Task<IEnumerable<TextChange>> GetCodeCleanupAndFormatChangesAsync(Document document, ICodeCleanupService codeCleanupService, CancellationToken cancellationToken)
        {
            var newDoc = await codeCleanupService.CleanupAsync(document, cancellationToken).ConfigureAwait(false);

            return await newDoc.GetTextChangesAsync(document, cancellationToken).ConfigureAwait(false);
        }
    }
}
